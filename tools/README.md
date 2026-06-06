# Binaires externes embarqués

Arboryn s'appuie sur deux outils en ligne de commande pour les empreintes
perceptuelles audio/vidéo (Incrément 5). Ils ne sont **pas** versionnés dans le
dépôt (binaires lourds, licences propres) : place-les ici, ou laisse-les
accessibles via le `PATH`.

| Outil       | Fichier attendu | Rôle                                       | Source |
|-------------|-----------------|--------------------------------------------|--------|
| Chromaprint | `fpcalc.exe`    | Empreinte acoustique des fichiers audio     | https://acoustid.org/chromaprint |
| FFmpeg      | `ffmpeg.exe`    | Extraction des keyframes pour l'empreinte vidéo | https://ffmpeg.org/download.html |

## Ordre de résolution (`ExternalToolResolver`)

1. Chemin explicite via variable d'environnement
   (`ARBORYN_FPCALC_PATH` pour fpcalc, `ARBORYN_FFMPEG_PATH` pour ffmpeg) ;
2. ce dossier `tools/` à côté de l'exécutable de l'application ;
3. recherche dans le `PATH` du système.

Si l'outil reste introuvable, la fonctionnalité correspondante est simplement
inactive (mode dégradé) — aucun plantage : `IAudioFingerprinter` renvoie `null`
et un avertissement est journalisé une fois.

## Installation rapide (Windows)

```powershell
# Chromaprint (fpcalc)
# Télécharge chromaprint-fpcalc-*-windows-x86_64.zip depuis acoustid.org,
# puis copie fpcalc.exe ici :
Copy-Item .\fpcalc.exe C:\dev\arboryn\tools\

# Vérifie
.\tools\fpcalc.exe -version
```

> Vérifie l'empreinte SHA-256 du binaire téléchargé avant de l'utiliser.
