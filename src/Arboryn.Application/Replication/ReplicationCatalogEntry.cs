using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Replication;

/// <summary>
/// Vue « read-model » d'un <c>LogicalFile</c> pour le calcul de placement (Inc 10, § 5.5) :
/// l'œuvre, son sujet de scope (catégorie / sous-catégorie / année), son chemin canonique
/// cible commun à tous les volumes, et l'ensemble de ses instances physiques réparties sur
/// les volumes. Type pur, assemblé en amont (repos + taxonomie) puis passé au calculateur.
/// </summary>
/// <param name="CanonicalRelativePath">
/// Chemin canonique cible, relatif à la racine de bibliothèque d'un volume (ex.
/// <c>Livres audio\Asimov\Fondation\Asimov - … .m4b</c>). <c>null</c> si l'œuvre ne peut être
/// placée (taxonomie absente ou champs requis manquants) : elle sera ignorée pour le placement.
/// </param>
/// <param name="Size">Taille canonique de référence (octets) — sert à l'impact espace et à la détection de conflit.</param>
public sealed record ReplicationCatalogEntry(
    LogicalFileId LogicalFileId,
    ScopeSubject Subject,
    string? CanonicalRelativePath,
    long Size,
    IReadOnlyList<ReplicaInstance> Instances);

/// <summary>Une instance physique d'un <c>LogicalFile</c> sur un volume donné.</summary>
/// <param name="RelativePath">Chemin courant relatif à la racine du volume.</param>
public sealed record ReplicaInstance(
    FileInstanceId Id,
    VolumeId VolumeId,
    string RelativePath,
    long Size);

/// <summary>Un volume et le périmètre de réplication qui lui est associé, pour le calcul.</summary>
/// <param name="Scope"><see cref="ScopeExpression.None"/> si le volume n'a pas de périmètre défini.</param>
public sealed record VolumeScope(
    VolumeId VolumeId,
    string VolumeName,
    VolumeStatus Status,
    ScopeExpression Scope);
