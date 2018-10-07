На стыке управляемого и неуправляемого миров
============================================

## Способы взаимодействия управляемого и неуправляемого кода

- C++/CLI
- COM
- P/Invoke в обе стороны
    - на примитивных типах
    - с использованием маршаллинга

## C++/CLI

Достоинства:

- простое потребление заголовочных файлов и API на C и C++
    - байки про
- возможность использования управляемого и неуправляемого кода в одном модуле
- ассемблерные вставки на x86
    - байки про интероп с Delphi и pascal calling convention

Недостатки:

- не кроссплатформенно
    - нет планов для поддержки в Mono
    - нет планов для поддержки в .NET Core

## COM

Стандартный кроссязыковой способ интеропа в Windows, не работает на остальных
платформах (хотя энтузиасты поддерживают несколько реализаций под другие
платформы).

COM-объекты часто хорошо доступны через `dynamic` в C#.

- https://fornever.me/ru/posts/2015-12-12-portable-com-usage.html

## P/Invoke

- простой вариант: напрямую мапить примитивные типы, указатели отображать на
  `IntPtr`
  - что на самом деле делает ключевое слово `unsafe` в C#
- сложный вариант: маршаллинг

### Размещение структур в памяти

- разные варианты `StructLayoutKind`
- немного про `Pack`
- blittable-типы (aka `unsafe`)
- выравнивание
- совет: не стесняйтесь писать тесты на layout структур, это может пригодиться

### Правила Marshal'а

- https://docs.microsoft.com/en-us/dotnet/framework/interop/copying-and-pinning

#### Строки

- кодировки
- виды строк (`string` vs `StringBuilder` в контексте маршаллинга)
- https://fornever.me/ru/posts/2017-09-20-clr-string-marshalling.html

### SafeHandle

### ICustomMarshaler

## Немножко IL-кода

### Unmanaged export

- https://github.com/ForNeVeR/SpySharp/blob/54c4d2c51a799d7e151a9db332dbe00a8e5bdb2f/SpySharp.Hooker/Hooker.il

### vararg-функции

- https://github.com/ForNeVeR/expert-cil-samples/blob/c80436e8dcc9763922c416625765de3b57b5296a/Simple.il#L52
