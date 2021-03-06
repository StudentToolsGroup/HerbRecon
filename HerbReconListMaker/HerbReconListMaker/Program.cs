﻿using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using HerbLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HerbReconListMaker
{
    internal class Program
    {
        private static HerbChecker herbChecker;

        [STAThread]
        private static void Main(string[] args)
        {
            while (true)
            {
                Console.Write(">");
                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input)) continue;
                var split = input.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 0) continue;
                var command = split[0].ToLower();
                var parameters = split.Skip(1).ToArray();
                if (command == "exit") break;
                switch (command)
                {
                    case "help":
                        Console.WriteLine("exit - exits the application");
                        Console.WriteLine("fetch - creates the object of herbs, pulled from the file in the parameter");
                        Console.WriteLine("check - opens a GUI which enables you to check the correctness of the data");
                        Console.WriteLine("help - shows this help");
                        break;
                    case "fetch":
                        if (parameters.Length < 1)
                        {
                            Console.WriteLine("You have to pass a file as the parameter");
                            break;
                        }
                        Fetch(parameters[0]);
                        break;
                    case "check":
                        Check();
                        break;
                    default:
                        Console.WriteLine("No command found. Use 'help'");
                        break;
                }
            }
        }

        private static void Fetch(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("File not found.");
                return;
            }
            var sr = new StreamReader(path);
            var collection = new HerbCollection();
            // probíhá pro každou řádku souboru
            while (!sr.EndOfStream)
            {
                var herb = new Herb();
                var wholeName = sr.ReadLine();
                // prohledá Wikipedii
                var results = WikipediaApiUtil.Search(wholeName);
                if (results == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Herb not found: " + wholeName);
                    Console.ResetColor();
                    var names = wholeName.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                    herb.Genus = names[0];
                    herb.Species = names.Skip(1).Aggregate((a, n) => a + " " + n);
                    collection.Herbs.Add(herb);
                    continue;
                }
                string title;
                // pokud je jen jeden výsledek vyhledávání, použijeme jej
                if (results.Length == 1)
                {
                    title = results[0].Title;
                }
                // jinak necháme uživatele vybrat správný článek
                else
                {
                    var selection =
                        SelectFromMultipleOptions("Choose the right Wikipedia article for the herb " + wholeName,
                            results.Select(r => $"{r.Title}\n\t{r.Summary}").ToArray());
                    title = results[selection].Title;
                }
                // vybereme rodové a druhové jméno byliny pomocí titulku vybraného Wikipedia článku
                var split = title.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                herb.Genus = split[0].ToLower();
                herb.Species = string.Join(" ", split.Skip(1).ToArray()).ToLower();
                // získáme obsah Wikipedia článku
                var page = WikipediaApiUtil.GetPageContentInJson(title);
                // nalezne v pravém boxu informace o bylině - čeleď a latinské jméno
                var taxobox = page["query"]["pages"].First.First["revisions"][0]["*"].ToString();
                var taxoboxRegex =
                    new Regex(@"čeleď = [\[]*(?<family>[^\] ]+)[\]]*[\s\S]*binomické jméno = (?<latin>[a-zA-z ]*)");
                var match = taxoboxRegex.Match(taxobox);
                if (!match.Success)
                {
                    herb.Family = null;
                    herb.LatinName = null;
                    continue;
                }
                var family = string.IsNullOrEmpty(match.Groups["family"].Value) ? null : match.Groups["family"].Value;
                herb.Family = family;
                var latinName = string.IsNullOrEmpty(match.Groups["latin"].Value) ? null : match.Groups["latin"].Value;
                herb.LatinName = latinName;
                // získáme obrázky
                var images = page["query"]["pages"].First.First["images"].Value<JArray>();
                if (images.Count > 0)
                {
                    // požadujeme jen obrázky s příponami .jpg, .png, .bmp-
                    herb.ImageUrls = images.Where(t =>
                    {
                        var tt = t["title"].ToString();
                        return tt.EndsWith(".jpg") | tt.EndsWith(".png") | tt.EndsWith(".bmp");
                    }).Select(t => WikipediaApiUtil.GetWikipediaImageUrl(t["title"].ToString())).ToList();
                }
                else
                {
                    herb.ImageUrls = null;
                }
                collection.Herbs.Add(herb);
            }
            if (!Directory.Exists("Output"))
            {
                Directory.CreateDirectory("Output");
            }
            // uloží získaná data
            File.WriteAllText("Output\\HerbsFormatted.json",
                JsonConvert.SerializeObject(collection, Formatting.Indented));
            File.WriteAllText("Output\\Herbs.json", JsonConvert.SerializeObject(collection, Formatting.None));
            // uloží MD5 daného souboru v hexadecimálníím formátu 0x0x
            File.WriteAllText("Output\\md5.txt", GetFileMd5("Output\\Herbs.json"));
            Console.WriteLine("Everything written to the Output folder.");
        }

        public static string GetFileMd5(string path)
        {
            string md5;
            using (var fs = new FileStream(path, FileMode.Open))
            {
                md5 =
                    BitConverter.ToString(MD5.Create().ComputeHash(fs))
                        .Replace("-", "").ToLower();
            }
            return md5;
        }

        private static void Check()
        {
            if (herbChecker == null)
            {
                Application.EnableVisualStyles();
                herbChecker = new HerbChecker();
                Application.Run(herbChecker);
            }
            else
            {
                herbChecker = new HerbChecker();
                herbChecker.ShowDialog();
            }
        }

        private static int SelectFromMultipleOptions(string message, string[] options)
        {
            Console.WriteLine(message);
            for (var i = 0; i < options.Length; i++)
            {
                Console.WriteLine($"\n[{i}]:");
                Console.WriteLine(options[i]);
            }
            var chosen = -1;
            while (chosen == -1)
            {
                try
                {
                    Console.Write("\nSelect a number: ");
                    chosen = int.Parse(Console.ReadLine());
                    if (chosen < 0 || chosen >= options.Length) throw new IndexOutOfRangeException();
                }
                catch
                {
                    Console.WriteLine("Wrong number.");
                }
            }
            return chosen;
        }
    }
}