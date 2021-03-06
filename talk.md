На стыке управляемого и неуправляемого миров
============================================

.NET считается «управляемой» платформой — это означает, что код выполняется в
виртуальной машине, которая должна следить за соблюдением некоторых правил
(корректность адресов объектов, к которым обращается программа, отсутствие
выхода за пределы массивов). На такой платформе программисту живётся очень
удобно — ровно до тех пор, пока ему не приходится начать интеропиться с кодом,
написанным вне платформы. Сейчас, с распространением .NET (Core) на новые
платформы, это становится ещё более важным — для новых платформ ещё не написано
такого большого количества managed-библиотек, и поэтому частенько приходится
делать свои обёртки для нативного кода.

К счастью, .NET обладает богатым инструментарием, который позволяет практически
прозрачно общаться с нативным кодом. Этот доклад познакомит вас с основными
техниками вызова нативных функций из .NET-приложений, особенностями размещения в
памяти структур, которыми может обмениваться управляемый и неуправляемый код, а
также некоторыми подводными камнями, которые обязательно оказываются на пути у
тех, кто начинает работу с нативным кодом из .NET.

В данном докладе я постараюсь говорить обо всех современных реализациях .NET: о
.NET Framework, Mono и .NET Core.

## Технологии

Представим, что мы пишем программу на C#, и вдруг появляется нативная
библиотека, с которой мы хотим поработать. В общем в .NET-платформе есть
несколько основных способов, как нам позвать нативный код:

- это **C++/CLI** — вариант языка C++, который компилируется в байт-код
  виртуальной машины .NET
- **COM** — бинарный кроссязыковый инструмент взаимодействия от Microsoft
- **P/Invoke** — встроенный в платформу механизм для вызова чужих функций

На самом деле есть и более экзотичные способы — можно, например, руками
нагенерировать машинного кода, пометить страничку с ним как выполняемую и
передать туда управление, но такие вещи мы в докладе рассматривать не будем :)

Основной упор в докладе я сделаю на последнем пункте, но для начала давайте
кратко рассмотрим все остальные.

## C++/CLI

Начнём с C++/CLI. Это язык, который основан на стандартном C++, но в него
добавили возможности для интеропа с системой типов .NET. Это управляемые
указатели (которые, по сути, соответствуют нашим ссылкам из C#):

```cpp
System::String ^foo = gcnew System::String("foobaz");
```

Также есть tracking references — это аналог наших `ref`:

```cpp
int x = 10;
int %ref = x;
if (System::Int32::TryParse("42", ref))
    System::Console::WriteLine(x);
```

(этот пример немножко бессмысленный, но я просто хотел показать кусочек
синтаксиса с объявлением tracking reference)

Если у вас уже есть заголовочные файлы от какой-то нативной библиотеки, то вы
просто берёте их, включаете в программу на C++/CLI директивой `#include`,
выставляете managed-интерфейс — и дело в шляпе.

```cpp
#include <Windows.h>

void function_called_from_managed_code() {
    HANDLE mutex = CreateMutex(nullptr, false, nullptr);
}
```

В этом языке различается managed- и unmanaged-код: есть специальные прагмы,
которыми мы размечаем свою программу, указывая секции с управляемым и нативным
кодом.

```cpp
#pragma managed
ref class ManagedClass {
    public:
        property int Foo {
            int get() { return 0; }
            void set(int value) {  }
        }
};

#pragma unmanaged
int __fastcall perform_wrapped_call() {
    int argument;
    __asm { mov argument, eax }
    return argument;
}
```

Типы у управляемого кода свои, для их объявления есть отдельные
синтаксические конструкции. Темплейты работают как для нативного, так и для
управляемого кода, при этом темплейты отличаются от генериков.

При этом нативный код реально компилируется в x86 или x86-64 машинный код,
который складывается в специальные места в нашей управляемой сборке. Помимо
прочего, компилятор от MS поддерживает ассемблерные вставки — впрочем, только на
x86. Но, по моему опыту, если вам это всё вообще потребовалось на Windows, то
скорее всего у вас есть какие-то либы от вендора, и они скорее всего как раз
x86. Об этом мы поговорим чуть дальше.

Существенным недостатком, который, на мой взгляд, убивает эту интересную
технологию на корню, является некроссплатформенность кода. На сегодняшний день
нет планов по поддержке в Mono, а официальные ответы MS про .NET Core таковы
(как видите, Windows-only, см. https://github.com/dotnet/coreclr/issues/659):

> **2015-04-10**: There is no plan to support C++/CLI with .NET Core.

> **2018-09-15**: You can track progress on Windows-only Managed C++ support in
> #18013.

## Байки из склепа

Расскажу историю, которая случилась со мной в продакшене. Был внешний API для
какой-то железки или иной системы, который был реализован на Delphi. У этого API
было несколько функций, которые в качестве аргументов принимали колбэки. Мы
обычно можем в качестве колбэков передавать наши managed-делегаты (как это
работает — мы ещё обсудим чуть позже). Описание API выглядело примерно так:

```delphi
type
 TMyCallback = function(number: Integer): Integer;

function DoCall(callback: TMyCallback): Integer; cdecl;
begin
  Result := callback(10);
end;
```

В объявлении одного из колбэков Delphi-программисты забыли поставить
спецификатор `stdcall`, и поэтому у него была стандартная для Delphi конвенция
вызова — которая, разумеется, в 2015 году уже никем официально не
поддерживалась, и нашим стандартным .NET-интеропом тоже. Одним из решений этой
проблемы было написание кода на C++/CLI, который с помощью ассемблерной вставки
реализует эту экзотическую конвенцию.

```cpp
// Func<int, int> ^callback;
// [UnmanagedFunctionPointer(CallingConvention::FastCall)]
// delegate int ExportFunction(int);
auto exportFunction = gcnew ExportFunction(
    callback,
    &Func<int, int>::Invoke);
auto ptr = Marshal::GetFunctionPointerForDelegate(
    safe_cast<Delegate^>(exportFunction));

prepare_call(static_cast<Callback*>(ptr.ToPointer()));
int result = doCallFunction(&perform_wrapped_call);
```

```cpp
Callback *CallbackInstance;
void prepare_call(Callback *arg) { CallbackInstance = arg; }

void clear_call() {
    CallbackInstance = nullptr;
}

int __fastcall perform_wrapped_call() {
    int real_argument;
    __asm { mov real_argument, eax }
    return CallbackInstance(real_argument);
}
```

Вот так, если очень хочется, можно писать на ассемблере для .NET.

## Component Object Model

Component Object Model, или просто COM — это стандартный кроссязыковой способ
интеропа в Windows, который не работает на остальных платформах (хотя реализации
на других платформах поддерживаются энтузиастами).

Обычно COM-библиотеки глобально регистрируются в системе, и после этого
референсы на них начинают работать во всех managed-проектах. Из TLB добывается
метаинформация, на основании которой делается managed-интерфейс для работы с
библиотечными типами. Это позволяет иметь автодополнение и какую-никакую
уверенность, что C#-код написан правильно.

```csharp
IComService instance = new IComService();
instance.HelloWorld();
```

(да-да, для COM-интерфейсов в C# можно официально вызывать конструктор)

Недостатки этой технологии: не кроссплатформенно, и плохо работает, если в
системе не установлены нужные типы — например, на билд-сервере собрать код,
который работает с COM, бывает сложновато, если вы не контролируете состояние
среды билд-сервера.

Однако эту проблему можно решить. Не все знают, что со многими COM-объектами
можно работать и без использования TLB, через `IDispatch` — например, таким
образом можно работать с MS Office (а больше половины всех случаев, когда
приходилось использовать COM, на моей памяти относятся именно к MS Office). В C#
это поддерживается через `dynamic` (и было одним из поводов ввести в язык данное
ключевое слово).

В одном случае мне довелось поддерживать смешанное решение: на тех машинах, на
которых установлена COM-библиотека, мы использовали строгую типизацию и работали
через TLB, а на других машинах (у сторонних разработчиков) использовали
`dynamic`. Это даже не очень сложно реализовать через условную компиляцию.
Предположим, что у нас есть такой код (да, если кто-то не знает, у
COM-интерфейсов официально можно вызывать конструкторы):

Для того, чтобы он умел также собираться с использованием `dynamic`, нужно
узнать GUID нужного нам типа, и написать что-то вроде такого:

```csharp
#if COM_LIBRARY_INSTALLED
    IComService instance = new IComService();
#else
    const string TypeGuid = "03653ea3-b63b-447b-9d26-fa86e679087b";
    Type type = Type.GetTypeFromCLSID(Guid.Parse(TypeGuid));
    dynamic instance = Activator.CreateInstance(type);
#endif

    instance.HelloWorld();
```

Здесь мы в зависимости от предопределённой константы описываем либо классическое
инстанцирование COM-объекта с помощью конструктора, либо его же, но с помощью
`dynamic`.

## P/Invoke

Ну и, наконец, перейдём к интересной части: про P/Invoke. Это мой любимый способ
взаимодействия с нативным кодом, потому что, во-первых, он достаточно гибок, но
не заставляет переписывать весь интеграционный слой на другом языке, а
во-вторых, он работает на всех платформах.

Работа с P/Invoke начинается, конечно, с атрибута `DllImport`, который в простом
варианте выглядит примерно так:

```csharp
[DllImport("StringConsumer.dll", CharSet = CharSet.Unicode)]
private static extern void PassUnicodeString(string str);
```

Давайте посмотрим на его свойства, которых значительно больше, чем можно было бы
предположить:

```csharp
public sealed class DllImportAttribute : Attribute
{
    public DllImportAttribute(string dllName) { /* ... */ }
    public string EntryPoint;
    public CharSet CharSet;
    public bool SetLastError;
    public bool ExactSpelling;
    public CallingConvention CallingConvention;
    public bool BestFitMapping;
    public bool PreserveSig;
    public bool ThrowOnUnmappableChar;
}
```

В конструктор этот атрибут принимает название библиотеки, из которой будут
вызываться функции: под Windows и macOS это полное имя файла, а под Linux и .NET
Core это только имя библиотеки без префикса и постфикса.

```csharp
[DllImport("tdjson.dll")] // → tdjson.dll   // Windows
[DllImport("tdjson")]     // → libtdjson.so // Linux, .NET Core
[DllImport("libtdjson.so")]  // → libtdjson.so // Linux, Mono
[DllImport("libtdjson.dylib")] // → libtdjson.dylib // macOS

[DllImport("__Internal")] // Mono only
```

Помимо этого, Mono также умеет загружать символы прямо из текущего файла (для
чего он, конечно, должен быть скомпилирован в нативный код) — это эдакий аналог
динамического связывания из C++/CLI.

```csharp
[DllImport("somelib.dll", EntryPoint = "MyFunctionName")]
[DllImport("somelib.dll", EntryPoint = "#123")] // by ordinal
[DllImport("somelib.dll",
    EntryPoint = "MessageBoxA",
    ExactSpelling = true)]
```

- `EntryPoint` — это имя функции, которая будет вызвана из библиотеки. Если вы
  хотите импортировать функцию по порядковому номеру (бывает такая надобность) —
  можно использовать имена типа `#123`.
- `ExactSpelling` нужен для того, чтобы CLR могла перестать угадывать название
  функции (`A` или `W`-варианты для WinAPI).

```csharp
public CharSet CharSet; // Auto, Ansi, Unicode
public bool BestFitMapping;
public bool ThrowOnUnmappableChar;
```

- `CharSet`: `Auto` / `Ansi` / `Unicode` (по умолчанию в CLI `Auto`, но в C# –
  `Ansi`).
- `BestFitMapping` контролирует подстановку символов для ANSI-кодировки.
- `ThrowOnUnmappableChar` — нужен для случаев, когда мы пытаемся скормить
  ANSI-функции юникод, который нормально не представляется в ANSI.

`SetLastError` — нужно выставлять для функций, после которых вызывателю хочется
вызвать `Marshal.GetLastWin32Error`; это нужно для случаев, когда сама CLR могла
бы затереть последнюю ошибку своими вызовами. Вот так можно получить текст
ошибки.

```csharp
int errorCode = Marshal.GetLastWin32Error();
string errorMessage = new Win32Exception(errorCode).Message;
```

`CallingConvention` — это соглашение о вызове нативной функции — то есть порядок
выделения и освобождения стека, порядок и способ передачи аргументов (через
стек, через регистры).

```csharp
public enum CallingConvention
{
    Winapi = 1,
    Cdecl = 2,
    StdCall = 3,
    ThisCall = 4,
    FastCall = 5,
}
```

`PreserveSig` влияет на интерпретацию возвращаемых значений типа `HRESULT`: если
`PreserveSig = true` (по умолчанию), то `HRESULT` будет возвращён как есть, а
если `false` — то невалидный `HRESULT` будет выброшен как исключение.

```csharp
[DllImport("my.dll", PreserveSig = true)]
HRESULT GetSomething(/*[out, retval]*/ BSTR *pRetVal);

[DllImport("my.dll", PreserveSig = false)]
extern static string GetSomething();
```

### Передача аргументов в натив

Мы разобрались с тем, как рантайм находит функции во внешних библиотеках. Теперь
поговорим о том, как в эти функции передаются аргументы. Как известно, нативный
код умеет принимать данные по значению или по указателю.

Для того, чтобы передать значение, мы просто передаём структуру, как мы привыкли
в .NET. Примитивные типы, как и другие структуры, тоже передаются по значению в
соответствии с выбранной вами calling convention.

- примитивные типы (`int`, `long`, `double` etc.)
- другие value types (структуры, enums)

А вот если мы хотим что-то передать по указателю, то ситуация становится
интереснее. По указателю в нативный код передаются:

- ссылочные типы (классы, делегаты)
- что угодно через `ref` или `out` (следует отметить, что с нативной стороны
  `ref` ничем не отличается от `out`)
- `IntPtr`
- unsafe-указатели

Да, если кто-то забыл, в C# есть возможность прямой работы с указателями.
Выглядит это вот так:

```csharp
int[] x = new int[10];
fixed (int* ptr = x) {
    Native.Call(ptr);
}
```

При этом структуры, которые передаются по указателю, на время выполнения
нативного вызова _пинятся_ в памяти — то есть GC не будет выполнять перемещение
таких объектов. Пининг, однако, не бесплатен, и стоит какой-то
производительности.

Все типы разделяются на категории blittable (у которых нативное и
managed-представление совпадают) и non-blittable. При передаче blittable-типа в
нативный код по указателю, туда передаётся просто указатель на нашу управляемую
память — копирования при этом не происходит. Для non-blittable типов может
потребоваться их преобразование и копирование, поэтому вызовы с такими типами
всегда обходятся дороже.

### Размещение структур в памяти

Теперь поговорим про то, как наши управляемые объекты раскладываются в памяти.
Обычно, когда вы делаете интероп с нативным кодом, у вас есть какие-то
заголовочные файлы или просто информация о том, как в памяти размещена нужная
структура. .NET позволяет управлять расположением полей в структуре, для этого
существует специальный атрибут `StructLayout`. Этот атрибут позволяет делать
всякие интересные вещи с нашими типами, что изредка может пригодиться и в
обычном коде, а не только в интеропе.

```csharp
[StructLayout(LayoutKind.Sequential)]
public class DBVariant {
    public byte type;
    public Variant Value;

    [StructLayout(LayoutKind.Explicit)]
    public struct Variant {
        [FieldOffset(0)] public byte bVal;
        [FieldOffset(0)] public byte cVal;
        [FieldOffset(0)] public ushort wVal;
        [FieldOffset(0)] public IntPtr pszVal;
        [FieldOffset(0)] public char cchVal;
    }
}
```

Первым аргументом у этого атрибута идёт `LayoutKind`, который бывает `Auto`,
`Sequential` и `Explicit`. `Sequential` располагает поля в структуре по порядку
(добавляя корректные паддинги), а `Explicit` позволяет вам самостоятельно
указать смещение для каждого поля.

Вот эта последняя особенность, во-первых, бывает очень полезна, когда у вас по
какой-то причине жёстко зашиты смещения (у меня такое было, когда мне спустили
сверху спецификацию на китайскую железку, которая умела только в свой бинарный
протокол — а у китайцев очень _интересные_ представления о том, как поля нужно
размещать в памяти). А во-вторых, она позволяет нам в .NET сделать настоящий
сишный union — то есть такую структуру, в которой на одном и том же месте могут
находиться объекты различного типа и конфигурации. У нас тут как раз пример
такого, который мне довелось написать, когда я делал managed-плагины для
мессенжера Miranda IM.

Второе важное свойство — это свойство `Pack`. Среднестатистический CPU очень
любит обращаться в памяти к адресам, которые выровнены определённым образом: как
правило, адреса объектов должны быть кратны размерам типов или размеру машинного
слова. Невыровненный доступ может просто обходиться дороже, или же вообще быть
запрещённым (как при некоторых режимах чтения на ARM). По умолчанию .NET
выравнивает данные в соответствии с выравниванием, которое делает компилятор C
от Microsoft, но мы можем это контролировать при помощи свойства `Pack`.

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 8)] // 16 bytes
public class DBVariant1 {
  public byte type;
  // padding: 7 bytes
  public IntPtr Pointer;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)] // 9 bytes
public class DBVariant2 {
  public byte type;
  // no padding
  public IntPtr Pointer;
}
```

Если мы говорим про memory layout, трудно не упомянуть ещё одну малоизвестную
возможность: fixed-массивы. Это такие массивы, которые выделяются не как обычные
— по ссылке и складываются в кучу, а такие, которые живут прямо в теле объекта,
в котором они объявлены. Для того, чтобы это работало, структуру нужно пометить
как `unsafe`, а количество элементов в массиве должно быть известно во время
компиляции. Стоит отметить, что эта возможность недаром помечается как `unsafe`:
за контроль границ массива вы сами несёте ответственность.

```c
// C
struct X {
    int Array[30];
};
```

```csharp
unsafe struct X {
    fixed int Array[30];
}
```

В завершение этой секции я дам совет: если у вас строгие требования к memory
layout — не стесняйтесь на это написать тесты. Сделать это можно, например, вот
так:

```csharp
struct Foo { public int x, y; }
Foo f = new Foo();
int offset1 = (byte*) &f.x - (byte*) &f;
Assert.Equal(0, offset1);
```

#### Строки

Теперь поговорим про строки. Все любят строки, правда? :)

Для начала обсудим, как нативный код работает со строками. Есть множество
вариантов: COM-строки, содержащие длину, однобайтовые строки, юникодовые (под
«юникодом» Microsoft традиционно понимает UTF-16).

Для этого есть специальный атрибут `MarshalAs`, и у него три ходовых значения
для строк:

```csharp
extern void Foo([MarshalAs(UnmanagedType.BStr)] string arg);
extern void Foo([MarshalAs(UnmanagedType.LPStr)] string arg);
extern void Foo([MarshalAs(UnmanagedType.LPWStr)] string arg);
```

Есть ещё варианты для выбора кодировки в зависимости от платформы (а-ля
`LPTStr`).

Стоит помнить, что наши строки в .NET — иммутабельные, так что стоит очень
аккуратно относиться к использованию API, которые могут эти строки помутировать.
Для примера рассмотрим функцию, которая реверсит переданную строку:

```cpp
#include <cwchar>
#include <xutility>

extern "C" __declspec(dllexport) void MutateString(wchar_t *string) {
    std::reverse(string, std::wcschr(string, L'\0'));
}
```

И напишем простой код на C#, который использует эту внешнюю функцию:

```csharp
[DllImport("Project1.dll", CharSet = CharSet.Unicode)]
private static extern void MutateString(string foo);

static void Main() {
  var myString = "Hello World 1";
  MutateString(myString);

  Console.WriteLine(myString.ToString()); // => 1 dlroW olleH
  Console.WriteLine("Hello World 1");     // => 1 dlroW olleH
}
```

Тут нужно вспомнить про интернирование строк: в некоторых случаях CLR будет
представлять несколько одинаковых строк в памяти единственным экземпляром. Так
вот и получается, что нативный код нам не просто помутировал строку, а поменял
_строковую константу_, и теперь везде в программе она отображается поломанной.
Этот код плохой, и так делать не следует!

В таком случае стоит использовать `StringBuilder`: он может работать с теми же
API, с которыми работают обычные строки, но в нём можно спокойно мутировать
данные, не боясь повредить никакие иммутабельные данные.

Вот правильный вариант такого же кода:

```csharp
[DllImport("Project1.dll", CharSet = CharSet.Unicode)]
private static extern void MutateString(StringBuilder foo);

static void Main() {
  var myString = new StringBuilder("Hello World 1");
  MutateString(myString);

  Console.WriteLine(myString.ToString()); // => 1 dlroW olleH
  Console.WriteLine("Hello World 1");     // => Hello World 1
}
```

Ну и неожиданный поворот: по непонятным лично для меня причинам, в структурах,
которые мы передаём в нативный код, всё работает совсем иначе: отчего-то в их
составе запрещено использовать `StringBuilder`, но `string` там будет работать
нормально:

```cpp
#include <cwchar>
#include <xutility>

struct S {
  wchar_t *field;
};
extern "C" __declspec(dllexport) void MutateStruct(S *s) {
  MutateString(s->field);
}
```

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct S {
    public string field;
}

[DllImport("Project1.dll", CharSet = CharSet.Unicode)]
private static extern void MutateStruct(ref S foo);

static void Main()
{
  S s = new S();
  s.field = "Hello World 2";
  MutateStruct(ref s);

  Console.WriteLine(s.field);         // => 2 dlroW olleH
  Console.WriteLine("Hello World 2"); // => Hello World 2
}
```

Вооружившись полученными знаниями, давайте попробуем побенчмаркать. Итак, мы
знаем, что строки по возможности передаются по указателю и без копирования.
Давайте посмотрим, насколько велики накладные расходы на копирование.

Напишем максимально простую DLL, которая будет принимать юникодовые и
ANSI-строки и просто игнорировать их.

```cpp
extern "C" __declspec(dllexport) void PassAnsiString(char *) {}
extern "C" __declspec(dllexport) void PassUnicodeString(wchar_t *) {}
```

И напишем небольшой бенчмарк, который будет использовать эти функции:

```csharp
[DllImport("StringConsumer.dll", CharSet = CharSet.Ansi)]
private static extern void PassAnsiString(string str);

[DllImport("StringConsumer.dll", CharSet = CharSet.Unicode)]
private static extern void PassUnicodeString(string str);

[Params(10, 100, 1000)]
public int N;

private string stringToPass;

[GlobalSetup]
public void Setup() => stringToPass = new string('x', N);

[Benchmark]
public void PassAnsiString() => PassAnsiString(stringToPass);

[Benchmark]
public void PassUnicodeString() => PassUnicodeString(stringToPass);
```

Результаты бенчмарка представлены ниже. Как видно, затраты на копирование для
строк длиной больше 10 символов уже становятся заметными на фоне простой
передачи указателя.

```
            Method |    N |        Mean |     Error |
------------------ |----- |------------:|----------:|
    PassAnsiString |   10 |    89.89 ns | 1.5052 ns |
 PassUnicodeString |   10 |    34.68 ns | 0.4818 ns |
    PassAnsiString |  100 |   167.77 ns | 3.4897 ns |
 PassUnicodeString |  100 |    36.37 ns | 0.7480 ns |
    PassAnsiString | 1000 | 1,032.29 ns | 7.2073 ns |
 PassUnicodeString | 1000 |    36.05 ns | 0.7446 ns |
```

### SafeHandle

Говоря о нативных API, неправильно будет не упомянуть `SafeHandle`. Это такая
штука, которая позволяет улучшить работу с системными хендлами. При работе с
хендлами часто приходится писать что-то в этом роде:

```csharp
// extern IntPtr CreateFile(…);
// extern void CloseHandle(IntPtr _);
IntPtr someHandle = CreateFile(…);
if (someHandle == IntPtr.Zero) throw new Exception("Invalid handle value");
try {
  // …
} finally {
  CloseHandle(someHandle);
}
```

Но специальный класс `SafeHandle` (и его вариации типа
`SafeHandleZeroOrMinusOneIsInvalid`), от которого вы можете наследоваться,
скрывает за собой весь этот бойлерплейт:

```csharp
class MyHandle : SafeHandleZeroOrMinusOneIsInvalid
{
  public MyHandle() : base(true) { }
  protected override bool ReleaseHandle() => CloseHandle(this.handle);
}
```

Если вы работаете с WinAPI-методами, которые возвращают хендлы, хорошей идеей
будет завернуть их в `SafeHandle`.

### ICustomMarshaler

Для случаев, когда стандартных возможностей маршалера вам недостаточно, есть
специальный интерфейс `ICustomMarshaler`.

```csharp
public interface ICustomMarshaler {
  object MarshalNativeToManaged(IntPtr pNativeData);
  IntPtr MarshalManagedToNative(object ManagedObj);
  void CleanUpNativeData(IntPtr pNativeData);
  void CleanUpManagedData(object ManagedObj);
  int GetNativeDataSize();
}
```

Это интерфейс, который содержит методы, которые будут вызываться рантаймом для
очистки и преобразования нативных и управляемых объектов. Что интересно — это
дополнительное требование к реализации: вы должны реализовать статический метод
`GetInstance` с определённой сигнатурой.

```csharp
public class MyMarshaler : ICustomMarshaler {
  public static ICustomMarshaler GetInstance(string cookie) => new MyMarshaler();
  // …
}

[MarshalAs(UnmanagedType.CustomMarshaler,
           MarshalType = "Foo.Bar.MyMarshaler",
           MarshalCookie = "Test")]
```

### Пара слов про вызов делегатов из нативного кода

Обсудим немного маршаллинг делегатов. Известно, что наши делегаты — это примерно
то же самое, что указатели на функции. И наши делегаты действительно можно
преобразовать к указателям на нативные функции!

Здесь есть одна проблемка: делегаты могут указывать много куда — например, на
инстансный метод. В нативном коде эта проблема иногда встречается: например,
программист на C++ не может понять, как ему указатель на метод передать
куда-нибудь, где у него спрашивают указатель на функцию без аргументов. И
решения в этом случае нет, потому что функция должна же как-то принимать
контекст.

У нас в .NET этой проблемы не будет: умный рантайм умеет скомпилировать функции
на лету, зашив туда все нужные указатели на контекст и пр.

Вот пример того, как можно вызвать нативную функцию, передав ей делегат:

```csharp
[UnmanagedFunctionPointer(CallingConvention.FastCall)]
delegate void MarshalableDelegate(int param);

[DllImport(…)]
static extern void NativeFunc(MarshalableDelegate x);

var myDelegate = new Foo(myObject.MyMethod);
NativeFunc(myDelegate);
```

Здесь есть очень интересный подводный камень: если этот указатель в нативном
коде где-то сохраняется и вызывается позже, это может вызвать проблемы. Дело в
том, что GC может собрать делегат сразу после вызова.

Поэтому вам нужно либо где-то сохранять ссылку на этот делегат, либо вызывать
`GC.KeepAlive`. Шаблон может быть примерно такой:

```csharp
var myDelegate = new Foo(myObject.MyMethod);
var context = NativeFuncBegin(myDelegate);
// ...
var result = NativeFuncEnd(context);
GC.KeepAlive(myDelegate);
```

## Немножко IL-кода

Напоследок покажу пару интересных фич, которые нельзя реализовать в C#, но в
самой платформе они доступны.

Во-первых, мало кто знает, но из DLL на .NET можно экспортировать функции так,
что нативные вызыватели смогут подгрузить вашу библиотеку и вызвать эти функции
стандартным способом.

В CIL это выглядит примерно так:

```
.assembly extern mscorlib { auto }
.assembly extern System { auto }
.assembly SpySharp.Hooks {}
.module SpySharp.Hooks.dll

.method assembly static native int modopt ([mscorlib]System.Runtime.CompilerServices.CallConvStdcall) HookProc(
  int32 nCode,
  native int lParam,
  native int wParam) {
  .vtentry 1 : 1
  .export [1] as HookProc

  ldc.i4.0
  ret
}
```

У меня есть один проект, в котором я решил написать часть кода на CIL специально
для того, чтобы использовать эту прекрасную фичу. Ну и, конечно, вы можете
постпроцессить свою сборку после компиляции, чтобы добавить нужные атрибуты и
метаданные и экспортировать функции.

Вторая занимательная штука — это вызов vararg-функций из .NET. Это сишные
функции типа `printf`, и на C# (насколько я знаю) сигнатуры таких функций для
`DllImport` описать нельзя. А на CIL — можно:

```
.method public static pinvokeimpl("msvcrt.dll" ansi cdecl)
vararg int32 printf(string) cil managed preservesig {}

// in method:
.locals init(int32 i, void* pb)

// printf(“%2.2d : %d\n”, i, *(int*)pb);
ldstr "%2.2d : %d\n"
ldloc.0
ldloc.1
ldind.i4
call vararg int32 printf(string, ..., int32, int32)
```

## Выводы

1. Не нужно бояться нативного кода.
2. По возможности стоит описывать код в безопасном стиле.
3. `StructLayout` — наш друг.
4. Со строками следует обращаться крайне осторожно.
5. Сохраняйте ссылки на делегаты.
6. Пишите тесты.
7. Можно даже писать тесты на memory layout.
