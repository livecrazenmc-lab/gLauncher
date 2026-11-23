# gLauncher

Лаунчер Minecraft Java Edition и Bedrock Edition для Windows (C# / WPF, .NET 8).
Играть можно только с лицензионным аккаунтом Microsoft.

## Сборка и запуск

```powershell
dotnet run --project src\GLauncher
```

## Перед первым входом: Client ID

Вход идёт через Microsoft, поэтому лаунчеру нужно своё приложение Azure:

1. [Зарегистрируйте приложение](https://learn.microsoft.com/entra/identity-platform/quickstart-register-app) в Microsoft Entra:
   * Supported account types — **Personal Microsoft accounts only**;
   * Authentication → **Allow public client flows = Yes** (нужен для входа по коду).
2. Подайте заявку Mojang на доступ к Minecraft API: <https://aka.ms/mce-reviewappid>.
   Пока заявку не одобрят, вход будет падать с ошибкой «приложение Azure не одобрено».
3. Вставьте Application (client) ID в лаунчере на вкладке «Настройки».

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
   и опубликует релиз. Установленные лаунчеры увидят его при следующем запуске.

Лаунчер проверяет `https://api.github.com/repos/livecrazenmc-lab/gLauncher/releases/latest` **без авторизации**,
поэтому репозиторий должен быть **публичным** — у приватного релизы видны только вам.
Обновление работает только в опубликованном `gLauncher.exe`; при запуске через `dotnet run` оно отключено.

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
