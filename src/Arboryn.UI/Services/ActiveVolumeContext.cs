using System;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.Services;

/// <summary>
/// Volume sur lequel portent les opérations courantes (scan, détection, uniformisation,
/// triage, enrichissement, inventaire). Partagé entre tous les ViewModels (Inc 9).
///
/// Initialisé sur le volume « default » : tant qu'aucun volume réel n'est enrôlé, le
/// comportement est strictement identique à l'avant-multi-volume (aucune régression).
/// Le dernier volume actif est mémorisé et restauré au démarrage (<see cref="InitializeAsync"/>),
/// s'il existe encore en base. Un scan enrôle/reconnaît le volume du dossier choisi et le rend
/// actif ; la page Volumes permet aussi de le sélectionner explicitement.
/// </summary>
public sealed class ActiveVolumeContext
{
    private const string ActiveVolumeKey = "active_volume_id";

    private readonly ISettingsRepository _settings;
    private readonly ILogger<ActiveVolumeContext> _logger;

    public ActiveVolumeContext(ISettingsRepository settings, ILogger<ActiveVolumeContext> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public VolumeId Current { get; private set; } = VolumeId.Default;

    public string CurrentName { get; private set; } = "Volume par défaut";

    /// <summary>Émis quand le volume actif change, pour permettre aux vues de se recharger.</summary>
    public event EventHandler? Changed;

    public void Set(VolumeId id, string name)
    {
        if (Current == id && string.Equals(CurrentName, name, StringComparison.Ordinal))
        {
            return;
        }

        Current = id;
        CurrentName = string.IsNullOrWhiteSpace(name) ? id.Value : name;
        Changed?.Invoke(this, EventArgs.Empty);
        _ = PersistAsync(Current);
    }

    /// <summary>
    /// Restaure le dernier volume actif persisté, s'il existe encore en base (sinon reste sur
    /// « default »). À appeler une fois au démarrage, avant l'affichage de la fenêtre.
    /// </summary>
    public async Task InitializeAsync(IVolumeRepository volumes, CancellationToken cancellationToken = default)
    {
        try
        {
            var stored = await _settings.GetAsync(ActiveVolumeKey, cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(stored) || stored == VolumeId.Default.Value)
            {
                return;
            }

            var record = await volumes.GetAsync(new VolumeId(stored), cancellationToken).ConfigureAwait(true);
            if (record is null)
            {
                return; // volume oublié / base réinitialisée → reste sur « default »
            }

            Current = record.Id;
            CurrentName = record.Name;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restauration du dernier volume actif impossible — « default » conservé.");
        }
    }

    private async Task PersistAsync(VolumeId id)
    {
        try
        {
            await _settings.SetAsync(ActiveVolumeKey, id.Value, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Persistance du volume actif {Volume} ignorée.", id.Value);
        }
    }
}
