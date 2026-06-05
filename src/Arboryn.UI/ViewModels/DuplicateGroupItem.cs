using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.UI.Xaml;

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

        if (kind == DuplicateGroupKind.FuzzyName)
        {
            // Cas flou : noms/tailles hétérogènes. Représentant = nom le plus court.
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
    }

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
