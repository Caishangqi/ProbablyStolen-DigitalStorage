using PininSharp.Utils;

namespace PininSharp.Elements
{
    internal interface IElement
    {
        IndexSet Match(string str, int start, bool partial);
    }
}
