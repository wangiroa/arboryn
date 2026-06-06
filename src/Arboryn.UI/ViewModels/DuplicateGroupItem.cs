using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Arboryn.UI.ViewModels;

/// <summary>Projection bindable d'un <see cref="DuplicateGroupView"/> pour la liste UI.</summary>
public sealed class DuplicateGroupItem
{
    public DuplicateGroupKind Kind { get; }

    /// <summary>Libellé court du type de groupe (badge).</summary>
    public string KindLabel { get; }

    public string Title { get; }

    /// <summary>Espace récupérable si l'on conserve la plus grosse copie : total − max.</summary>
    public long ReclaimableBytes { get; }

    /// <summary>La confirmation par hash n'a de sens que pour un groupe flou.</summary>
    public Visibility ConfirmByHashVisibility =>
        Kind == DuplicateGroupKind.FuzzyName ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<DuplicateMemberItem> Members { get; }

    /// <summary>
    /// Catégories de filtre couvertes par le groupe : union des catégories possibles de chaque
    /// copie (d'après l'extension). Sert au filtrage par type de média de la vue des doublons.
    /// </summary>
    public IReadOnlySet<MediaFilterType> MediaTypes { get; }

    /// <summary>Vrai si le groupe doit s'afficher pour le filtre demandé (<c>null</c> = tous types).</summary>
    public bool Matches(MediaFilterType? filter) => filter is null || MediaTypes.Contains(filter.Value);

    public DuplicateGroupItem(DuplicateGroupView view)
        : this(
            view.Kind,
            view.Members
                .Select((m, index) => new DuplicateMemberItem(m.Id, m.Path, m.Size, shouldDelete: index > 0))
                .ToList())
    {
    }

    private DuplicateGroupItem(DuplicateGroupKind kind, IReadOnlyList<DuplicateMemberItem> members)
    {
        Kind = kind;
        KindLabel = LabelFor(kind);
        Members = members;
        MediaTypes = members
            .SelectMany(m => MediaFilterClassifier.FromExtension(m.Path.Extension))
            .ToHashSet();

        var totalBytes = members.Sum(m => m.Size);
        var largest = members.Max(m => m.Size);
        ReclaimableBytes = totalBytes - largest;

        Title = BuildTitle(kind, members);
    }

    /// <summary>
    /// Nouvel item après suppression de certaines copies (les membres restants
    /// conservent leur état de sélection), ou <c>null</c> s'il reste ≤ 1 copie.
    /// </summary>
    public DuplicateGroupItem? WithoutMembers(ISet<string> removedMemberIds)
    {
        var remaining = Members.Where(m => !removedMemberIds.Contains(m.Id.Value)).ToList();
        return remaining.Count <= 1 ? null : new DuplicateGroupItem(Kind, remaining);
    }

    private static string LabelFor(DuplicateGroupKind kind) => kind switch
    {
        DuplicateGroupKind.ExactName => "Exact",
        DuplicateGroupKind.FuzzyName => "Flou",
        DuplicateGroupKind.ExactHash => "Hash ✓",
        DuplicateGroupKind.Perceptual => "Perceptuel",
        _ => kind.ToString(),
    };

    private static string BuildTitle(DuplicateGroupKind kind, IReadOnlyList<DuplicateMemberItem> members)
    {
        var reclaimable = members.Sum(m => m.Size) - members.Max(m => m.Size);

        // Cas flou et perceptuel : noms/tailles hétérogènes. Représentant = nom le plus court.
        if (kind is DuplicateGroupKind.FuzzyName or DuplicateGroupKind.Perceptual)
        {
            var representative = members
                .OrderBy(m => System.IO.Path.GetFileName(m.DisplayPath).Length)
                .First();
            return $"≈ {System.IO.Path.GetFileName(representative.DisplayPath)} — {members.Count} fichiers similaires " +
                   $"— récupérable {SizeFormatter.Humanize(reclaimable)}";
        }

        // Cas exact / hash : tailles identiques.
        var size = members[0].Size;
        return $"{System.IO.Path.GetFileName(members[0].DisplayPath)} — {members.Count} copies × {SizeFormatter.Humanize(size)} " +
               $"— récupérable {SizeFormatter.Humanize(reclaimable)}";
    }
}

/// <summary>Une copie au sein d'un groupe ; <see cref="ShouldDelete"/> est éditable (case à cocher).</summary>
public sealed class DuplicateMemberItem : INotifyPropertyChanged
{
    private readonly string _baseLabel;
    private bool _shouldDelete;
    private string? _hash;

    public FileInstanceId Id { get; }

    public FilePath Path { get; }

    public long Size { get; }

    public string DisplayPath { get; }

    /// <summary>Répertoire parent, utilisé pour la sélection par répertoire prioritaire.</summary>
    public string Directory { get; }

    public DuplicateMemberItem(FileInstanceId id, FilePath path, long size, bool shouldDelete)
    {
        Id = id;
        Path = path;
        Size = size;
        DisplayPath = path.Value;
        _baseLabel = $"{path.Value} — {SizeFormatter.Humanize(size)}";
        Directory = System.IO.Path.GetDirectoryName(path.Value) ?? string.Empty;
        _shouldDelete = shouldDelete;
        IsImage = MediaClassifier.FromExtension(path.Extension) == MediaCategory.Photo;
    }

    /// <summary>Vrai si le fichier est une image — déclenche l'aperçu miniature dans la comparaison.</summary>
    public bool IsImage { get; }

    public Visibility ThumbnailVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlyphVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Miniature de l'image, construite paresseusement à la première lecture (sur le thread UI,
    /// via x:Bind). Décodée à ~220 px pour limiter la mémoire. <c>null</c> hors images.
    /// </summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded)
            {
                return _thumbnail;
            }

            _thumbnailLoaded = true;
            if (IsImage)
            {
                try
                {
                    _thumbnail = new BitmapImage(new Uri(Path.Value)) { DecodePixelWidth = 220 };
                }
                catch (Exception)
                {
                    _thumbnail = null;
                }
            }

            return _thumbnail;
        }
    }

    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;

    /// <summary>Chemin + taille, et empreinte courte une fois calculée (copies identiques = même #hash).</summary>
    public string Label => _hash is null ? _baseLabel : $"{_baseLabel}   —   #{_hash}";

    /// <summary>Empreinte SHA-256 courte (8 caractères), renseignée par « Confirmer par hash ».</summary>
    public string? Hash
    {
        get => _hash;
        set
        {
            if (_hash != value)
            {
                _hash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hash)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            }
        }
    }

    /// <summary>Coché = à envoyer à la corbeille. Notifie pour refléter les changements programmés.</summary>
    public bool ShouldDelete
    {
        get => _shouldDelete;
        set
        {
            if (_shouldDelete != value)
            {
                _shouldDelete = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldDelete)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
