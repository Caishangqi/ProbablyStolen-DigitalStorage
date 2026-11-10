using System;

namespace PininSharp;

internal interface IDictLoader
{
    void Load(Action<char, string[]> feed);
}
