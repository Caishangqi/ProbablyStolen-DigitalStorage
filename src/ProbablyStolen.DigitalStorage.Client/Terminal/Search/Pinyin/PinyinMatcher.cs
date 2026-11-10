using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace PininSharp;

internal readonly struct PinyinKeyboardOptions
{
    internal PinyinKeyboardOptions(PinyinKeyboard keyboard, bool zh2Z, bool sh2S, bool ch2C,
        bool ang2An, bool ing2In, bool eng2En, bool u2V)
    {
        Keyboard = keyboard switch
        {
            PinyinKeyboard.Daqian => Keyboard.Daqian,
            PinyinKeyboard.Xiaohe => Keyboard.Xiaohe,
            PinyinKeyboard.Ziranma => Keyboard.Ziranma,
            PinyinKeyboard.Sougou => Keyboard.Sougou,
            PinyinKeyboard.Guobiao => Keyboard.Guobiao,
            PinyinKeyboard.Microsoft => Keyboard.Microsoft,
            PinyinKeyboard.PinyinPp => Keyboard.PinyinPp,
            PinyinKeyboard.Ziguang => Keyboard.Ziguang,
            _ => Keyboard.Quanpin
        };
        Zh2Z = zh2Z; Sh2S = sh2S; Ch2C = ch2C; Ang2An = ang2An; Ing2In = ing2In; Eng2En = eng2En; U2V = u2V;
    }
    internal Keyboard Keyboard { get; }
    internal bool Zh2Z { get; }
    internal bool Sh2S { get; }
    internal bool Ch2C { get; }
    internal bool Ang2An { get; }
    internal bool Ing2In { get; }
    internal bool Eng2En { get; }
    internal bool U2V { get; }
}

internal sealed class PinyinMatcher
{
    private readonly PinIn _pinIn;
    internal PinyinMatcher(string dictionary)
    {
        if (string.IsNullOrWhiteSpace(dictionary)) throw new InvalidDataException("The pinyin dictionary is empty.");
        _pinIn = new PinIn(new TextDictionaryLoader(dictionary));
        _pinIn.Load();
    }
    internal void Configure(SearchConfiguration configuration)
    {
        _pinIn.Configure(new PinyinKeyboardOptions(configuration.Keyboard, configuration.Zh2Z, configuration.Sh2S,
            configuration.Ch2C, configuration.Ang2An, configuration.Ing2In, configuration.Eng2En, configuration.U2V));
    }
    internal bool Contains(string source, string query) => _pinIn.Contains(source, query);

    private sealed class TextDictionaryLoader : IDictLoader
    {
        private readonly string _text;
        internal TextDictionaryLoader(string text) => _text = text;
        public void Load(Action<char, string[]> feed)
        {
            foreach (var line in _text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var separator = line.IndexOf(':');
                if (separator != 1 || line.Length <= 2) throw new InvalidDataException("Malformed pinyin dictionary line.");
                var readings = line.Substring(separator + 1).Replace(',', ' ').Trim()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (readings.Length == 0) throw new InvalidDataException("Pinyin dictionary entry has no reading.");
                foreach (var reading in readings)
                {
                    if (reading.Length < 2 || reading[reading.Length - 1] < '0' || reading[reading.Length - 1] > '5')
                        throw new InvalidDataException("Malformed pinyin reading.");
                }
                feed(line[0], readings);
            }
        }
    }
}
