Aware
=====

Aware is a language that is likely to not exist for some time, and may never exist, as I am currently learning language design and compilers with D#. However, I wanted a "design document" to park language design ideas in that I find interesting without necessarily needing to face the consequences of fully thinking the idea through. As such, until this document reaches a point where a base language design has actually been solidified, you will see that this document contains many loosely-connected half-baked ideas. However, it is my hope that these ideas may mature to form a real language.

# Ideas
- static analysis as type codegen
- everything as primitives
- control flow as data
- explicit closure

## static analysis as type codegen
We know from C# nullable reference types that static analysis can inform type checking at compile-time without adding any additional load at runtime.

```csharp
class Person(string Name) { }
```

```csharp
Person? writer = null;
writer.Name = "John Doe"; // compile-time warning or error
```

```csharp
Person? writer = null;
writer ??= new Person("John Doe");

writer.Name = "Eric Wong"; // allowed
```

If you're not familiar with the feature. Essentially, you demarcate reference types with `?`s to annotate that a variable can possibly be null. When you try to perform an operation using a `Person?` that would result in a `NullReferenceException`, you receive a compiler error or warning, depending on how you've configured your project. When you check that a variable of type `Person?` is not null, from that point forward the compiler treats the variable as a `Person`, meaning it assumes it's not `null` and doesn't spit warnings or errors as long as it can guarantee it maintains a not-`null` state.

What this means is that if you have a method that takes a `Person?`, and you check that it isn't `null`, if you then pass it to a method that takes a `Person`, you can syntactically guarantee that a reference is not null and eliminate unnecessary checks within your application code.

This feature is very useful, and you could imagine a similar feature for `int`s. Imagine a case where you perform arbitrary division on user input. You want to filter out `0`s to avoid divide by zero errors. You could define a `PositiveInt` which an `int` contextually gets "elevated" to over some scope such that the compiler can infer that, barring reflection, that `int` variable never stores anything less than `1`.

One tradeoff from a feature like this is that it requires you, the programmer, to use more specific types. For cases like nullability, I see this as an upside, not a downside. However, already with cases of both simple nullability and primitive checking, types for simple primitives get a bit cluttered (`PositiveInt` vs `int`). However, imagine a language where the programmer is given the freedom to specify their own context-aware types.

```
type IntHigherThan5 n = n > 5
```

In this case, `n`'s "base type" gets inferred to an `int`. The compiler looks at points of mutation or domain checking where it can validate `IntHigherThan5`'s predicate will pass.

You can imagine the problems that this could create in library code. F# developers may already be used to specifying [Units of Measure](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/units-of-measure), where types that contain bit-equivalent values which could otherwise be added, subtracted, multiplied, etc., together are narrowed to a specific type to disallow unit conversion errors by embedding units into the type system. What if two different developers created their own `IntHigherThan5`s and the compiler was unable to coalesce them from its design (aside from the fact the two different names could be confusing for unaware programmers)?

```
// package A
type IntHigherThan5 n = n > 5

bool AddIntHigherThan5(int a, IntHigherThan5 n)
{
    return a + n;
}

// package B
type IntIsHigherThan5 n = n > 5
bool SubtractIntIsHigherThan5(int a, IntIsHigherThan5 n)
{
    return a - n;
}

// package C which consumes A and B 
IntHigherThan5 num = 6;
_ = SubtractIntIsHigherThan5(5, num); // should this be allowed?
// now imagine if the types were units of measure (centimeter vs inch)
```

One potential solution would reduce the complexity, but likely the power, that programmers could create with predicate-based context-aware types, is to push the type generation process into the compiler.

```
int x = ...; // any

x = 40; // x = 40

if (Random.NextBool())
{
    x = 20;
}
```

```
int x = ...; // any
// any
x = 40; // 40
// 40
if (Random.NextBool()) // 40
{
    x = 20; // 20
    // 20
}
// 40 or 20
```

As illustrated, metadata about the state of each variable gets attached to blocks for which that state can be guaranteed. This metadata can then be converted to soft "types", which can be treated as subset types of the parent types they originate from and can be coalesced when predicates are logically equivalent. Now with codegen:

```
int x = ...;

type <>int001 int x = x == 40
<>int001& _int001__x = x = 40; // non-allocating

if (Random.NextBool())
{
    type <>int002 int x = x == 20
    <>int002& _int002__x = x = 20;
}

type <>int003 int x = x == 40 || x == 20
<>int003& _int003__x = x;
```

This type information that was generated could be said to be generated from "top-down". In that sense, it could provide some useful tooltip information as to the possible state of `x`. However, with standard types, the moment we pass `x` down to another method, all this work becomes for naught because the user cannot specify the generated types and is forced to go back to using `x`.

What if, however, types could also be generated from the "bottom-up"? Take the following user-code:

```
int DivideAByN(int a, int n)
{
    return a / n;
}
```

What if we want to statically prevent divide by zero errors?

To be clear, without codegen, we want this to result in an error, because `n` can be `0`. But that's okay, because we can just push the precondition to the definition of `n`, which in this case is in the method signature.

```
type <>int004 int x = x != 0
int DivideAByN(int a, <>int004 n)
{
    return a / n;
}
```

It's worth mentioning, that pushing "type requirements" up to the definition of `n` does not require `n` to be in a method signature. This segment

```
int n = ...;
_ = 3 / n;
```

would become:

```
int n = <>int004& _int004__n = ...; // <-- if == 0, error!
type <>int004 int x = x != 0
_ = 3 / n;
```

It's also worth mentioning that `n` doesn't have to always be a `<>int004` (`!= 0`) - it just has to be when it's passed to `DivideAByN`.

Back to our original example, because these are "soft" types, not true strict user-defined types, all the compiler has to do (I am vastly underestimating the work this would take for complex types) is validate type equality from top down to bottom up - particularly at function calls.

```
int x = ...;

type <>int001 int x = x == 40
<>int001& _int001__x = x = 40; // non-allocating

if (Random.NextBool())
{
    type <>int002 int x = x == 20
    <>int002& _int002__x = x = 20;
}

type <>int003 int x = x == 40 || x == 20
<>int003& _int003__x = x;

DivideAByN(4, _int003__x); // is { x | x == 40 || x == 20 } a subset of { x | x != 0 }? Yes!

type <>int004 int x = x != 0
int DivideAByN(int a, <>int004 n)
{
    return a / n;
}
```

Considering this as a complete program (aside from the `...`), unfortunately, we lose all that juicy state that tells us `n` is actually `x == 40 || x == 20`, since that type is wholly stricter than `x != 0`. I haven't fully thought this out but there seem to be two possibilities here.
1. It doesn't matter, because the moment we need to ensure `n` is of some stricter type (one such way would be a C# `switch` or F# `match` expression with no `_` case), the compiler can pick up on this and ensure `n` is whatever we need.
2. We can look at all points a function is (legally) called from and take the union of the contextual types being passed in.

An interesting consequence of the second option is that simply calling a function could change its signature, which would likely cause problems in distributing native compiled code (although all this work is being done at compile-time). Still, I do not see an area where the second option is worth the cost and complexity.

One big problem with a language like this is that it puts a lot of work on the compiler, both to completely validate state across variable lifetimes *and* give accurate reporting at type conflict error locations. Given the right tooling, hopefully, a language like this could lead to much more intentional code without relying on immutability or complex types.

## everything as primitives
There is another idea that comes as a useful side effect of [the last formless idea](#static-analysis-as-type-codegen), though it has not solidified even nearly as much and should probably be treated as even more unfeasible. The core:

What separates a `byte` from a `bool[8]`?

You probably get what I'm going for here, and byte alignment and such probably make it not worth it, especially for bit level operations.

However, I'm looking for someone to give me a good argument as to why booleans should exist. If a boolean takes a byte which encodes 256 possibilities and only uses two, then it should be very easy to refactor and add a third possibility like when using an `enum`. But instead we get v2s of APIs adding boolean flags upon boolean flags unwilling to make breaking changes.

## data as control flow
This comes from what you might expect given [the first section](#static-analysis-as-type-codegen) mentions partial types being generated based on whether there is a `switch` expression or not, but I kinda like `switch` expressions as control flow, and think they work well for discarding invalid state. That being said, I wouldn't necessarily replace all `try-catch` blocks with them.

## explicit closure
Closures are fine, but in non-functional languages, I generally find that I can more quickly get a better idea what traditional object-oriented code is doing than code that uses a lot of anonymous functions. Anonymous functions also cannot (?) be inlined by the compiler. I don't know exactly how this translates to language design yet, but prefer local (nested) functions before anonymous functions to be explicit about when and how you're capturing state from the current context, then elevating to `struct`s or `class`es when ready. Maybe the language should enforce this?