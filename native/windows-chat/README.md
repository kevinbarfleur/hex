# Hex Chat pour Windows

**Deux pressions rapides sur Ctrl** démarrent une dictée pour Twitch depuis le jeu. **Deux nouvelles pressions sur Ctrl** la terminent. La transcription s’insère dans le brouillon, le focus reste dans le chat et tu choisis quand appuyer sur Entrée. **Ctrl + Espace** dicte toujours dans l’application active, même lorsque Twitch est ouvert.

## Démarrer

1. Après installation, ouvre **Hex** depuis le menu Démarrer. La version portable se lance avec **hex.exe** dans le dossier des deux exécutables. Au premier démarrage, termine la configuration de Hex, choisis ta langue et attends que le modèle soit prêt.
2. Laisse l’onglet du chat Twitch visible dans sa propre fenêtre Helium ou Edge. Retourne dans le jeu et appuie deux fois brièvement sur la même touche **Ctrl**, gauche ou droite. Parle, puis refais **double Ctrl** pour insérer le brouillon.
3. Si le champ est absent, si plusieurs chats sont possibles ou si la fenêtre refuse le focus, ce même double Ctrl lance la dictée dans l’application active. Termine-la aussi avec double Ctrl. Le raccourci ordinaire **Ctrl + Espace** conserve le mode Maintenir ou Bascule choisi dans Hex.

## Choisir un chat précis

La détection automatique reconnaît une fenêtre de navigateur dont le titre contient « Twitch », une page Twitch et un champ de message visible. Si le titre ou le libellé est personnalisé, ou si plusieurs chats sont ouverts, clique dans ton champ puis appuie une fois sur **Ctrl + Alt + F8**. Le menu **Twitch…** de l’icône Hex affiche ensuite « Champ mémorisé ».

La sélection manuelle reste valable pour la session. Une fenêtre peut être déplacée entre deux écrans. Si la page est rechargée, mémorise de nouveau le champ. Pour cibler uniquement une fenêtre, coche **Mémoriser uniquement la fenêtre** sous **Twitch…** dans le menu de l’icône Hex avant de la sélectionner. **Détection automatique de Twitch** efface la sélection manuelle.

Si la cible disparaît ou perd le focus *après* le début d’une dictée ciblée, Hex conserve le texte dans le presse-papiers. Tu peux alors le coller avec **Ctrl + V**. **Désactiver le ciblage** rend immédiatement les prochaines dictées au fonctionnement ordinaire de Hex.

Hex doit recevoir le raccourci principal. Si un autre outil de dictée intercepte **Ctrl + Espace**, quitte cet outil ou change son raccourci dans ses paramètres.

## Compiler ce fork

Depuis la racine du dépôt, suis les [prérequis Windows](../../docs/windows.md#requirements-for-source-builds), puis exécute les commandes suivantes dans PowerShell. Le sélecteur utilise .NET Framework 4.8 fourni avec Windows.

```powershell
$env:HEX_LOCAL_BUILD = '1'
cargo build --locked --release --bin hex
./native/windows-chat/build.ps1 -OutDir ./target/release
```

Lance `target/release/hex.exe app`. Le sélecteur enregistre **Ctrl + Alt + F8**. Le moteur de Hex gère **double Ctrl**, **Ctrl + Espace**, l’audio et le collage.

Pour générer un installateur Windows x64 qui enregistre Hex dans le menu Démarrer et les applications installées, exécute aussi les deux commandes suivantes depuis la racine du dépôt.

```powershell
cargo build --locked --release -p hex-console
./scripts/build-windows-installer.ps1 -Executable ./target/release/hex.exe -Console ./target/release/hex-console.exe -ChatPicker ./target/release/HexChat.exe -Version 3.4.0 -Arch x86_64 -OutDir ./dist
```

Hex démarre le ciblage à chaque ouverture, y compris au démarrage de Windows si l’option correspondante est activée. Quitter Hex ferme aussi le ciblage. Les mises à jour de ce fork s’installent manuellement.
