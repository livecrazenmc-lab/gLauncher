# gLauncher

Лаунчер Minecraft Java Edition и Bedrock Edition для Windows (C# / WPF, .NET 8).
Играть можно только с лицензионным аккаунтом Microsoft.

## Установка

Скачайте `gLauncher-Setup.exe` со страницы [Releases](https://github.com/livecrazenmc-lab/gLauncher/releases/latest) и запустите.
Права администратора не нужны: лаунчер ставится в `%LocalAppData%\Programs\gLauncher`, создаёт ярлык в «Пуске»
(и по желанию на рабочем столе) и появляется в «Установленных приложениях». Дальше он обновляется сам.
При удалении аккаунты, профили и миры в `%APPDATA%\.glauncher` сохраняются.

Там же лежит портативный `gLauncher.exe` — его можно запускать без установки.

## Сборка и запуск

```powershell
dotnet run --project src\GLauncher
```

Установщик собирается [Inno Setup 6](https://jrsoftware.org/isinfo.php) из [installer/gLauncher.iss](installer/gLauncher.iss)
после `dotnet publish` в папку `publish` (так же, как в release.yml):

```powershell
ISCC.exe /DAppVersion=0.3.0 installer\gLauncher.iss
```

## Вход через Microsoft: Client ID

Игрокам ничего настраивать не нужно: Client ID приложения Azure вшит в лаунчер
(свойство `GLauncherClientId` в [Directory.Build.props](Directory.Build.props)). Это не секрет —
публичный клиент без пароля, его можно хранить в репозитории.

Как получить свой Client ID (один раз, делает разработчик):

1. Зайдите на <https://entra.microsoft.com> (или portal.azure.com) → **App registrations** → **New registration**.
   * Name: `gLauncher`;
   * Supported account types: **Personal Microsoft accounts only**;
   * Redirect URI можно не указывать.
2. В приложении: **Authentication** → **Allow public client flows** → **Yes** → Save (нужно для входа по коду).
3. Скопируйте **Application (client) ID** со страницы Overview.
4. Подайте заявку Mojang на доступ к Minecraft API: <https://aka.ms/mce-reviewappid> (указать Client ID и Tenant ID).
   Пока заявку не одобрят, вход заканчивается ошибкой «приложение лаунчера ещё не одобрено Mojang».
5. Впишите ID в `GLauncherClientId` в Directory.Build.props и выпустите новую версию — все лаунчеры получат её автообновлением.

Для тестов можно указать свой Client ID в «Настройки → Для разработчиков» — он перекрывает встроенный.
## Что умеет

| | |
|---|---|
| Аккаунты | Несколько аккаунтов Microsoft, вход по коду (device code), токены зашифрованы DPAPI |
| Лицензия | Java — по наличию профиля Minecraft; Bedrock — по entitlement `product_minecraft_bedrock` / `game_minecraft_bedrock` |
| Java Edition | Профили (своя папка, версия, RAM, JVM-аргументы, разрешение), релизы и снапшоты, автозагрузка Java от Mojang |
| Модлоадеры | Fabric, Quilt, Forge, NeoForge |
| Контент | Поиск и установка с Modrinth: моды (с обязательными зависимостями), ресурспаки, шейдеры; включение/выключение и удаление |
| Сборки | Импорт `.mrpack` из файла или из поиска Modrinth (создаёт профиль), экспорт профиля в `.mrpack` |
| Удобства | Автовход на сервер, свернуть/закрыть лаунчер после запуска, отчёт о сбое игры, предупреждение о нехватке RAM |
| Обновления | Проверка GitHub Releases при запуске и установка новой версии в один клик |
| Bedrock | Проверка установки Minecraft for Windows / Preview и запуск |

## Выпуск новой версии (автообновление)

1. Поднимите `<Version>` в [Directory.Build.props](Directory.Build.props), например до `0.3.0`, и закоммитьте.
2. Создайте и отправьте тег:
   ```powershell
   git tag v0.3.0
   git push origin v0.3.0
   ```
3. GitHub Actions ([release.yml](.github/workflows/release.yml)) соберёт `gLauncher.exe` (один файл, .NET внутри)
   и установщик `gLauncher-Setup.exe` и опубликует релиз. Установленные лаунчеры увидят его при следующем запуске:
   они скачивают ассет `gLauncher.exe`, сверяют SHA-256 и подменяют exe на месте.

Лаунчер проверяет `https://api.github.com/repos/livecrazenmc-lab/gLauncher/releases/latest` **без авторизации**,
поэтому репозиторий должен быть **публичным** — у приватного релизы видны только вам.
Обновление работает только в опубликованном `gLauncher.exe`; при запуске через `dotnet run` оно отключено.

## Оформление

Тема «Блоки» лежит в [src/GLauncher/Themes](src/GLauncher/Themes):

* [Theme.xaml](src/GLauncher/Themes/Theme.xaml) — палитра (раздел «Палитра» в начале файла) и стили всех контролов:
  кнопки-блоки с фаской, поля, списки, вкладки, прокрутка, прогресс в виде шкалы опыта.
  Именованные стили: `PlayButton`, `DangerButton`, `SmallButton`, `ConsoleList`, `FieldLabel`, `SectionTitle`, `HintText`, `WarningText`.
* [PixelTextures.cs](src/GLauncher/Themes/PixelTextures.cs) — пиксельные текстуры 16×16 (камень, глубинный сланец, земля, дёрн, доски),
  нарисованные кодом. В XAML: `Background="{themes:PixelTexture Kind=Stone, TileSize=64}"`.

Ассеты Mojang (шрифт, текстуры, звуки) не используются — всё нарисовано заново.

## Структура

```
src/GLauncher.Core     логика без UI
  Auth/                вход Microsoft → Xbox → Minecraft, хранилище аккаунтов
  Java/                версии, загрузка файлов, Java, модлоадеры, аргументы запуска
  Content/             Modrinth, моды/ресурспаки/шейдеры профиля, .mrpack
  Updates/             обновление лаунчера из GitHub Releases
  Bedrock/             проверка и запуск Bedrock
src/GLauncher          WPF-интерфейс (MVVM, CommunityToolkit.Mvvm)
  Views/*.xaml         разметка вкладок — здесь меняется внешний вид
  ViewModels/          состояние и команды, к которым привязаны Views
  App.xaml             место для общих стилей (ResourceDictionary)
```

Данные лаунчера: `%APPDATA%\.glauncher` (versions, libraries, assets, runtime, instances, настройки).
Установленный с Modrinth контент профиля описан в `<папка игры>\.glauncher\content.json`.

## Ограничения

* Forge и NeoForge ставятся их официальными установщиками (`--installClient`). Очень старые версии Forge так не устанавливаются;
  NeoForge поддерживает Minecraft 1.20.2 и новее.
* У подписчиков Game Pass лицензия Bedrock может не отображаться в entitlements.

