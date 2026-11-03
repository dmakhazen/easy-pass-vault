# Сторонние компоненты

- KeePassLib.Standard 2.57.1: неофициальная адаптация KeePassLib, Dominik Reichl / Werdes. https://www.nuget.org/packages/KeePassLib.Standard/2.57.1 . Исходники: https://github.com/werdes/KeePassLib.Standard . Метаданные пакета указывают commit `80c12888884702538074d1827fe23173417a1f0e` и лицензию `GPL-2.0-or-later`.
- В приложении собирается локальная копия исходников этого commit в `vendor/KeePassLib`. Изменения описаны в `vendor/KeePassLib/CHANGES.md`, текст лицензии сохранён в `vendor/KeePassLib/LICENSE`.
- Microsoft .NET / WPF и обновлённые Microsoft-пакеты зафиксированы в `packages.lock.json`.
- SharpZipLib — транзитивная зависимость адаптации; сжатие хранилищ отключено.

Перед распространением EXE нужно включить применимые тексты лицензий и выполнить требования лицензий библиотек, включая предоставление соответствующих исходников и изменений. Выбор лицензии собственного проекта должен учитывать зависимости. Публикация пока не выполняется.

KeePassLib.Standard не равнозначен официальному релизу KeePass. Проверка известных уязвимостей NuGet не заменяет аудит.
