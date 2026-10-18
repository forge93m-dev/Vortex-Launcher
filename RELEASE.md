# Выпуск и обновление Vortex Launcher

Рабочая система обновления появилась в тестовых сборках во время разработки **0.2.0 — Beta 2**. Первая публичная **0.1.0** с ошибочной кнопкой обновления должна быть заменена вручную один раз. Затем обновления запускаются через **Settings → Updates → Check for updates → Install update**. Полный список изменений: `Installer/RELEASE-NOTES.md`.

## Подготовка, без публикации

В GitHub Actions есть ручной workflow **Prepare launcher release packages (manual)**. Он не запускается при push и не создаёт GitHub Release: только собирает и проверяет файлы выпуска.

- Windows: автономный ZIP для нового updater и Inno Setup EXE для старых Windows-версий.
- macOS: подписанные на Mac ZIP для Apple Silicon и Intel.
- `latest.json`: точная версия, ссылки, размер и SHA-256 каждого ZIP.

Исходники, включая `Client/VortexClient/dist/vortex-client-26.3-fabric-0.1.21.jar`, должны быть загружены в репозиторий перед запуском workflow. Нет необходимости устанавливать Java/Minecraft на CI.

## Варианты для Mac

1. **Бета без Apple Developer ID** — workflow по умолчанию ставит корректную локальную подпись. Это исправляет некорректную структуру подписи, но НЕ является notarization. Другу может потребоваться разрешение **Open Anyway** через System Settings → Privacy & Security. Терминал на его Mac не нужен.
2. **Обычный публичный выпуск** — включить `notarize_mac`, предоставить Developer ID Application сертификат и секреты ниже. Workflow подписывает приложение, ждёт статуса Accepted от Apple и прикрепляет ticket.

Секреты для notarized-выпуска, только в GitHub Actions Secrets, не в исходниках и не в чате:

- `MAC_CERTIFICATE_P12` — экспортированный сертификат с приватным ключом, base64.
- `MAC_CERTIFICATE_PASSWORD`, `MAC_KEYCHAIN_PASSWORD`.
- `MAC_SIGN_IDENTITY` — Developer ID Application identity.
- `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD`.

Для сертификата нужен доступ к Apple Developer Program. Подписать и notarize на Windows невозможно; для этого используется macOS runner. Подробности: [Apple Developer ID](https://developer.apple.com/help/account/certificates/create-developer-id-certificates), [Apple Gatekeeper](https://support.apple.com/en-us/102445).

## Локальная сборка

```powershell
./publish-win-x64.ps1
./publish-macos.ps1 -Runtime osx-arm64
```

Windows создаёт `release/VortexLauncher-0.1.2-win-x64.zip`. На Windows Mac ZIP будет **unsigned cross-build**, только для проверки упаковки: не отправлять его как готовую подписанную обнову. На Mac скрипт создаёт корректно подписанный ZIP в `release`.

Генерация манифеста из реальных ZIP:

```powershell
./Installer/GenerateUpdateManifest.ps1
```

Она требует подписанный Apple Silicon пакет. `-AllowPartial` используется только для локального тестирования Windows; частичный манифест не следует публиковать для друга с Mac.

## Публикация — отдельное действие

После явного решения выпустить обновление:

1. Создать GitHub Release `v0.1.2` и прикрепить **все файлы** из CI-артефакта `release-ready-for-publication`.
2. Только после загрузки файлов заменить `latest.json` в корне main на сгенерированный манифест.
3. Проверить публичные ссылки, размеры и SHA-256. Не ставить новую версию манифеста со ссылками на старый установщик.

Сейчас workflow и подготовка файлов сами ничего не публикуют.

## Как устанавливается обновление

ZIP потоково скачивается по HTTPS и проверяется SHA-256, затем проверяются пути, CPU и версия приложения. На Mac дополнительно проверяется codesign. Помощник запускается из новой автономной сборки, ждёт завершения старого лаунчера и заменяет только приложение.

- Windows: только `Vortex Launcher.exe`, атомарная замена и резервный EXE.
- Mac: только текущая `Vortex Launcher.app`, резервная app и возврат старой при ошибке переименования.
- Миры, моды, config, аккаунты и runtime не являются целями замены.
- Бэкап и результат записываются в журнал; после перезапуска результат виден в Settings.
- Отмена загрузки не меняет установленное приложение.
- Запущенный Minecraft/установка сборки блокируют обновление.
- Debug/не-single-file сборки открывают папку готового пакета для ручной замены, без автоматического закрытия.

На реальном Mac этот путь ещё нужно протестировать. Если папка приложения недоступна для записи, нужна ручная замена через Finder — updater не использует sudo, не отключает Gatekeeper и не удаляет quarantine.

