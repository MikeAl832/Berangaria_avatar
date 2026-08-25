# Berangaria Avatar Runtime

Локальный Unity 2022.3-проект VRM-аватара для `D:\Projects\Berangaria_agent`.

## Обычный запуск

Unity Editor не нужен. Запусти `start-avatar.bat` либо общий
`D:\Projects\Berangaria_agent\start-with-avatar.bat`.

## Работа в Unity

1. Запусти `open-unity-editor.bat` либо открой `D:\Projects\Berangaria_avatar`
   через Unity Hub. Лаунчер использует проверенный редактор Unity `2022.3.62f3`.
2. Для новой модели выбери `Berangaria > Replace Avatar VRM...` и укажи VRM 1.0.
3. Unity скопирует модель как `Assets/Characters/current.vrm`, пересоберёт сцену и откроет её.
4. `Berangaria > Open and Play Avatar` запускает проверку в редакторе.
5. `Berangaria > Build Windows Avatar` обновляет автономный Windows-плеер.

Python отправляет на `127.0.0.1:17891` только состояние и уровень громкости.
Тексты, изображения, микрофонное аудио и PCM-байты в Unity не передаются.

## Репозиторий и релизы

`current.vrm` хранится через Git LFS, потому что сцена напрямую ссылается на его
Unity GUID. Черновые VRM-файлы исключены из Git. `Library`, `Logs`, `Builds` и
остальное локальное состояние Unity также не публикуются.

Текущая VRM Meta запрещает перераспределение модели, поэтому репозиторий и его
GitHub Releases должны оставаться приватными. Перед публичной публикацией нужно
переэкспортировать VRM с подходящими правами и отдельно проверить лицензии всех
пользовательских частей персонажа.
