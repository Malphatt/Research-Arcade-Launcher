using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ArcademiaGameLauncher.Windows;
using Newtonsoft.Json.Linq;

namespace ArcademiaGameLauncher.Utils
{
    internal class EmojiParser
    {
        readonly Dictionary<string, string> _colonedEmojis;
        readonly Regex _colonedRegex;

        public EmojiParser()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "ArcademiaGameLauncher.Assets.json.emojis.json";

            // read embedded JSON resource from assembly
            using Stream stream =
                assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException("Embedded resource not found.", resourceName);
            using StreamReader reader = new(stream, Encoding.UTF8);
            string json = reader.ReadToEnd();

            // parse JSON into JArray (array of JSON objects)
            JArray data = JArray.Parse(json);

            _colonedEmojis = data.OfType<JObject>()
                .ToDictionary(
                    // key dictionary by coloned short names
                    c => ":" + ((JValue)c["short_name"]).Value.ToString() + ":",
                    c =>
                    {
                        var unicodeRaw = ((JValue)c["unified"]).Value.ToString();
                        var chars = new List<char>();
                        // some characters are multibyte in UTF32, split them
                        foreach (var point in unicodeRaw.Split('-'))
                        {
                            // parse hex to 32-bit unsigned integer (UTF32)
                            uint unicodeInt = uint.Parse(
                                point,
                                System.Globalization.NumberStyles.HexNumber
                            );
                            // convert to bytes and get chars with UTF32 encoding
                            chars.AddRange(
                                Encoding.UTF32.GetChars(BitConverter.GetBytes(unicodeInt))
                            );
                        }
                        // this is resulting emoji
                        return new string(chars.ToArray());
                    }
                );
            // build huge regex (all 1500 emojies combined) by join all names with OR ("|")
            _colonedRegex = new Regex(string.Join("|", _colonedEmojis.Keys.Select(Regex.Escape)));
        }

        public string ReplaceColonNames(string input)
        {
            // replace match using dictoinary
            return _colonedRegex.Replace(input, match => _colonedEmojis[match.Value]);
        }
    }
}
