namespace Arboryn.Application.Abstractions;

/// <summary>
/// Partage sûr du catalogue entre PC (Inc 13, A2). La base de travail reste toujours locale ;
/// le partage se fait par actions explicites — <see cref="ExportAsync"/> vers un emplacement
/// partagé/amovible, et <see cref="ScheduleImport"/> depuis un tel emplacement — via l'API SQLite
/// Online Backup, ce qui produit un fichier cohérent sans jamais ouvrir la base en direct sur un
/// dossier cloud/réseau (source de corruption).
/// </summary>
public interface ICatalogTransfer
{
    /// <summary>
    /// Exporte une copie cohérente de la base de travail vers <paramref name="destinationPath"/>
    /// (écrit d'abord un fichier temporaire, puis le déplace atomiquement).
    /// </summary>
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Planifie le remplacement de la base de travail par <paramref name="sourcePath"/> : l'import
    /// prend effet au <b>prochain démarrage</b> (avant toute ouverture de la base). Ne modifie rien
    /// immédiatement.
    /// </summary>
    void ScheduleImport(string sourcePath);
}
