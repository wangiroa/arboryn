using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de la page Historique : liste les opérations récentes du journal (renommages,
/// déplacements, copies, suppressions, write-back de métadonnées) avec leur statut, la plus
/// récente d'abord. Lecture seule ; l'annulation reste sur les pages concernées (Doublons,
/// Uniformisation, Plan de placement).
/// </summary>
public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private const int MaxRows = 300;

    private readonly IOperationJournal _journal;
    private readonly IVolumeRepository _volumes;
    private readonly ILogger<HistoryViewModel> _logger;

    private bool _isBusy;
    private string _summaryText = string.Empty;

    public HistoryViewModel(
        IOperationJournal journal,
        IVolumeRepository volumes,
        ILogger<HistoryViewModel> logger)
    {
        _journal = journal;
        _volumes = volumes;
        _logger = logger;
    }

    public ObservableCollection<HistoryRowItem> Operations { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public bool IsEmpty => !IsBusy && Operations.Count == 0;

    public bool HasOperations => Operations.Count > 0;

    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        OnPropertyChanged(nameof(IsEmpty));
        try
        {
            var volumes = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(true);
            var volumeNames = volumes.ToDictionary(v => v.Id.Value, v => v.Name, StringComparer.Ordinal);

            var recent = await _journal.GetRecentAsync(MaxRows, cancellationToken).ConfigureAwait(true);
            Operations.Clear();
            foreach (var op in recent)
            {
                Operations.Add(new HistoryRowItem(op, volumeNames));
            }

            OnPropertyChanged(nameof(HasOperations));

            var undone = recent.Count(o => o.Status == OperationStatus.Undone);
            var failed = recent.Count(o => o.Status is OperationStatus.Failed);
            SummaryText = recent.Count == 0
                ? "Aucune opération enregistrée pour l'instant."
                : $"{recent.Count} opération(s) récente(s)"
                  + (undone > 0 ? $" · {undone} annulée(s)" : string.Empty)
                  + (failed > 0 ? $" · {failed} en échec" : string.Empty)
                  + (recent.Count == MaxRows ? $" (limité aux {MaxRows} dernières)" : string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement de l'historique");
            SummaryText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasOperations));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Ligne d'affichage d'une opération dans l'historique.</summary>
public sealed class HistoryRowItem
{
    public HistoryRowItem(Operation op, IReadOnlyDictionary<string, string> volumeNames)
    {
        var when = op.ExecutedAt ?? op.CreatedAt;
        WhenText = when.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
        KindLabel = KindToLabel(op.Kind);
        StatusLabel = StatusToLabel(op.Status);
        StatusForeground = StatusToBrush(op.Status);
        Description = DescribeTarget(op);
        Detail = DescribeDetail(op, volumeNames);
    }

    public string WhenText { get; }

    public string KindLabel { get; }

    public string StatusLabel { get; }

    public Brush StatusForeground { get; }

    public string Description { get; }

    public string Detail { get; }

    private static string DescribeTarget(Operation op)
    {
        var path = op.NewPath?.Value ?? op.OldPath?.Value;
        return string.IsNullOrEmpty(path) ? "(fichier inconnu)" : Path.GetFileName(path);
    }

    private static string DescribeDetail(Operation op, IReadOnlyDictionary<string, string> volumeNames)
    {
        var parts = new List<string>();

        if (op.Kind is OperationKind.Rename or OperationKind.Move
            && op.OldPath is not null && op.NewPath is not null)
        {
            parts.Add($"{op.OldPath.Value} → {op.NewPath.Value}");
        }
        else if (op.NewPath?.Value is { } np)
        {
            parts.Add(np);
        }
        else if (op.OldPath?.Value is { } opth)
        {
            parts.Add(opth);
        }

        if (op.Kind == OperationKind.Copy && op.SourceVolumeId is { } src && op.TargetVolumeId is { } tgt)
        {
            parts.Add($"{VolumeName(src.Value, volumeNames)} → {VolumeName(tgt.Value, volumeNames)}");
        }
        else if (op.SourceVolumeId is { } only)
        {
            parts.Add(VolumeName(only.Value, volumeNames));
        }

        return string.Join("  ·  ", parts);
    }

    private static string VolumeName(string id, IReadOnlyDictionary<string, string> volumeNames)
        => volumeNames.TryGetValue(id, out var name) ? name : "volume ?";

    private static string KindToLabel(OperationKind kind) => kind switch
    {
        OperationKind.Rename => "Renommage",
        OperationKind.Move => "Déplacement",
        OperationKind.Copy => "Copie",
        OperationKind.Delete => "Suppression",
        OperationKind.MetadataWriteback => "Métadonnées",
        _ => kind.ToString(),
    };

    private static string StatusToLabel(OperationStatus status) => status switch
    {
        OperationStatus.Pending => "En attente",
        OperationStatus.InProgress => "En cours",
        OperationStatus.Completed => "Effectuée",
        OperationStatus.Failed => "Échec",
        OperationStatus.Cancelled => "Annulée",
        OperationStatus.Undone => "Annulée (undo)",
        _ => status.ToString(),
    };

    private static Brush StatusToBrush(OperationStatus status)
    {
        var key = status switch
        {
            OperationStatus.Completed => "ArborynTextPrimaryBrush",
            OperationStatus.Failed => "ArborynCautionBrush",
            OperationStatus.Pending or OperationStatus.InProgress => "ArborynAccentTextBrush",
            _ => "ArborynTextSecondaryBrush",
        };
        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    }
}
