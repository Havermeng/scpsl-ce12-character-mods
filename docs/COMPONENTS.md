# Компоненты / Components

## SCP-939

| Компонент | По умолчанию | Что меняется |
|---|---|---|
| Core | Обязателен | Совместимость, общий код и настройки |
| Outfit | Включён | Наша одетая модель Scp939Dressed (22); обычная Scp939 (16) сохраняется |
| Interactions | Включён | Поглаживания, объятия, прогулка за руку, меню E |
| Gifts | Включён | Подарки и передача предмета; требует Interactions |
| Dates | Включён | Стол, декорации, свидание и ужин; требует Interactions |
| Dialogue | Включён | Реплики и развитие симпатии; требует Interactions |
| Own WAV voice | Выключен | Ваши WAV; требует Interactions и Dialogue; готовых записей нет |

Отключение компонента удаляет его ресурсы и отключает соответствующие действия, включая серверную проверку. Общие C#-классы, от которых зависят функции, остаются в Core до полного удаления мода. Изменение состава требует остановленного Play.

## SCP-3114

| Компонент | По умолчанию | Что меняется |
|---|---|---|
| Core | Обязателен | SCP-3114 (41), механики, HUD, F1, анимации |
| Maid | Включён | Scp3114Dressed (42), модель и труп |
| Natural spawning | Выключен | Обычный спавн роли 41 в раунде |

Серверный параметр scp3114_allow_natural_spawn явно переопределяет выбор Natural spawning. Одетые роли 22/42 всегда исключены из автоматического выбора SCP.

Both mods have independent selections. Dependencies are enforced. Changing one mod preserves the other's saved selection. Shared runtime classes remain in Core until full removal; optional assets/flags are removed when deselected. Explicit server configuration can override the natural-spawn default.
