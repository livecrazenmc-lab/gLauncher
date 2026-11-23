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
| Java Edition | Профили (своя папка, версия, RAM, JVM-аргументы, разрешение), релизы и снапшоты, Fabric, Forge, автозагрузка Java от Mojang |
| Bedrock | Проверка установки Minecraft for Windows / Preview и запуск |

## Структура

```
src/GLauncher.Core     логика без UI
  Auth/                вход Microsoft → Xbox → Minecraft, хранилище аккаунтов
  Java/                версии, загрузка файлов, Java, Fabric/Forge, аргументы запуска
  Bedrock/             проверка и запуск Bedrock
src/GLauncher          WPF-интерфейс (MVVM, CommunityToolkit.Mvvm)
  Views/*.xaml         разметка вкладок — здесь меняется внешний вид
  ViewModels/          состояние и команды, к которым привязаны Views
  App.xaml             место для общих стилей (ResourceDictionary)
```

Данные лаунчера: `%APPDATA%\.glauncher` (versions, libraries, assets, runtime, instances, настройки).

## Ограничения

* Forge ставится официальным установщиком (`--installClient`). Очень старые версии Forge так не устанавливаются.
* У подписчиков Game Pass лицензия Bedrock может не отображаться в entitlements.
