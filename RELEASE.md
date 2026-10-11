# Выпуск Vortex Launcher

## 1. Изменить версию

В `Vortex Launcher.csproj` увеличь `<Version>`:

```xml
<Version>0.1.1</Version>
```

Версия должна увеличиваться при каждом публичном обновлении.

## 2. Собрать автономную программу

В PowerShell из корня проекта:

```powershell
.\publish-win-x64.ps1
```

Результат появится в `publish\win-x64`. Это автономная Windows x64-сборка: другу не понадобится устанавливать .NET.

## 3. Создать установщик

Установи Inno Setup, открой `Installer\VortexLauncher.iss` и нажми Compile. Готовый файл появится в `release`.

Установщик ставит программу в `%LOCALAPPDATA%\Programs\Vortex Launcher`, поэтому обновления не требуют прав администратора.

## 4. Публикация обновления

Загрузи `VortexLauncherSetup-0.1.0.exe` в GitHub Releases. Для следующей версии измени номер в проекте и в `.iss`, снова выполни публикацию и создай новый Release.

Папки `instances`, `config`, `assets` и `runtime` сейчас находятся рядом с установленным приложением. При обычном обновлении Inno Setup они сохраняются. Не используй режим удаления пользовательских файлов при обновлении.

## 5. Уведомление о новой версии

Опубликуй `latest.json.example` под именем `latest.json` по постоянной ссылке, например в GitHub Pages или в корне репозитория. Затем один раз добавь эту ссылку в `config\settings.json`:

```json
{
  "UpdateManifestUrl": "https://example.com/VortexLauncher/latest.json"
}
```

При старте лаунчер сравнит версию из манифеста с установленной версией и покажет: `Установите новое обновление: Vortex Launcher X.Y.Z`.
