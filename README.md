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
| Скины | Библиотека скинов, добавление из PNG и по нику игрока, 3D-превью с вращением, установка скина (обычные/тонкие руки), сброс, выбор плаща |
| Сборки | Импорт `.mrpack` из файла или из поиска Modrinth (создаёт профиль), экспорт профиля в `.mrpack` |
| Удобства | Автовход на сервер, свернуть/закрыть лаунчер после запуска, отчёт о сбое игры, предупреждение о нехватке RAM |
| Обновления | Проверка GitHub Releases при запуске и установка новой версии в один клик |
| Bedrock | Выбор версии «последний выпуск / Minecraft Preview», запуск, установка и обновление через Microsoft Store |
| Другие игры | Minecraft Dungeons, Dungeons II и Legends: поиск установки (Microsoft Store/Xbox и Steam) и запуск |
| Интерфейс | Боковая панель игр и вкладки как в официальном лаунчере; списки изменений всех игр с сервера Mojang; официальные иконки игр |

## Темы

* **Сезонная** (по умолчанию) — сама меняет оформление по дате: весна, лето, осень, зима, а в праздники —
  Новый год (20.12–10.01), Хэллоуин (24.10–02.11), Пасха (±7 дней от православной Пасхи). Праздник важнее времени года.
* **Блоки** — постоянная тема без сезонов.
* Эксклюзивные темы для людей из благодарностей — см. ниже.

## Благодарности и плюшки

Список людей из благодарностей — файл [credits.json](credits.json) в корне репозитория. Лаунчер скачивает его с GitHub
при каждом запуске, поэтому, чтобы добавить или убрать человека, достаточно поправить файл на GitHub — релиз не нужен.
Поля: `uuid` (UUID аккаунта Minecraft), `role` (подпись), `greeting` (личное приветствие в шапке).

Если активный аккаунт есть в списке (сверка по UUID), человек получает:

* блок «Для разработчиков» в настройках;
* эксклюзивные темы: ★ Legends, ★ Dungeons, ★ Нижний мир, ★ Энд;
* золотой ник и значок роли в карточке игрока, личное приветствие в шапке;
* пасхалку: 5 быстрых щелчков по логотипу gLauncher.
## Версии

Формат **0.Y.X**:

| Что изменилось | Как меняется версия | Пример |
|---|---|---|
| Мелкое обновление — исправления (фиксы) | X + 1 | 0.3.0 → 0.3.1 → 0.3.2 |
| Обычное обновление — новые функции и т. п. | Y + 1, X сбрасывается в 0 | 0.3.2 → 0.4.0 |

* Первое число всегда **0** и не меняется.
* Числа не переносятся в следующий разряд: после 9 идёт **10** — 0.3.9 → 0.3.10, 0.9.0 → 0.10.0.
* Лаунчер сравнивает версии как числа, поэтому 0.10.0 считается новее 0.9.0.
## Выпуск новой версии (автообновление)

1. Поднимите `<Version>` в [Directory.Build.props](Directory.Build.props) по правилам из раздела «Версии» (например, до `0.4.0`) и закоммитьте.
2. Создайте и отправьте тег:
   ```powershell
   git tag v0.4.0
   git push origin v0.4.0
   ```
3. GitHub Actions ([release.yml](.github/workflows/release.yml)) соберёт `gLauncher.exe` (один файл, .NET внутри)
   и установщик `gLauncher-Setup.exe` и опубликует релиз. Установленные лаунчеры увидят его при следующем запуске:
   они скачивают ассет `gLauncher.exe`, сверяют SHA-256 и подменяют exe на месте.

Лаунчер проверяет `https://api.github.com/repos/livecrazenmc-lab/gLauncher/releases/latest` **без авторизации**,
поэтому репозиторий должен быть **публичным** — у приватного релизы видны только вам.
Обновление работает только в опубликованном `gLauncher.exe`; при запуске через `dotnet run` оно отключено.

## Оформление

Стиль «Изометрия» лежит в [src/GLauncher/Themes](src/GLauncher/Themes):

* [Theme.xaml](src/GLauncher/Themes/Theme.xaml) — стили всех контролов: стеклянные карточки, градиентные кнопки,
  вкладки-«таблетки», боковая панель. Именованные стили: `Card`, `PlayButton`, `AccentButton`, `DangerButton`,
  `SmallButton`, `ConsoleList`, `FieldLabel`, `SectionTitle`, `HintText`, `WarningText`, `HeroTitle`, `Chip`.
* [ThemeManager.cs](src/GLauncher/Themes/ThemeManager.cs) — цвета каждой темы (основная, сезонная, эксклюзивные) и вид частиц.
* [ParticleField.cs](src/GLauncher/Themes/ParticleField.cs) — частицы фона: снежинки, осенние листья, лепестки,
  пух одуванчика и светлячки, угольки и летучие мыши, искры.
* [FontManager.cs](src/GLauncher/Themes/FontManager.cs) — шрифт интерфейса: по умолчанию пиксельный, в настройках можно
  выбрать любой шрифт Windows.

Иконка — вектор: исходники в [assets/icon](assets/icon) (.svg), в окне она нарисована как `Img.Logo` в Theme.xaml,
а `gLauncher.ico` и `gLauncher-Setup.ico` содержат кадры 16–256 px, каждый отрисован отдельно.

Пиксельный шрифт — [Monocraft](https://github.com/IdreesInc/Monocraft) © Idrees Hassan, лицензия
[SIL Open Font License 1.1](src/GLauncher/Assets/Fonts/OFL.txt); встроен в сборку. Это свободный шрифт в стиле Minecraft,
а не шрифт самой игры.

Ассеты Mojang (шрифт, текстуры, звуки) не используются.
Официальные иконки игр в боковой панели в репозиторий и сборку не входят: лаунчер скачивает их из каталога
Microsoft Store при запуске (кэш на неделю), а без сети показывает свои пиксельные иконки.

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







