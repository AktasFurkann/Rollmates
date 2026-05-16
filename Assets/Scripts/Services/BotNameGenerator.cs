using System;
using System.Text;

namespace LudoFriends.Services
{
    /// <summary>
    /// Bot'lar icin gercekci kullanici adlari uretir.
    /// Pattern karisik: ahmetk04, jpinkman00, emre_07, selin2003, shadow99, burak.acar vs.
    /// </summary>
    public static class BotNameGenerator
    {
        private static readonly string[] TurkishNames =
        {
            "ahmet", "mehmet", "burak", "emre", "selin", "deniz", "can", "elif",
            "kerem", "ozan", "berk", "sude", "yusuf", "merve", "furkan", "hande",
            "ali", "ece", "mert", "ayse", "zeynep", "kaan", "ipek", "ege",
            "baris", "tuna", "doruk", "esra", "kemal", "aysu", "hakan", "naz",
            "onur", "irem", "umut", "deren", "cem", "yagmur", "tolga", "buse"
        };

        private static readonly string[] ForeignNames =
        {
            "alex", "mike", "ryan", "kate", "leo", "max", "jake", "nina",
            "kim", "ben", "jay", "sara", "tom", "luna", "nick", "lisa",
            "dan", "rose", "ace", "lily", "noah", "emma", "kai", "anna",
            "jess", "owen", "ivy", "felix", "ruby", "milo", "zoe", "finn"
        };

        private static readonly string[] GamerTags =
        {
            "shadow", "blaze", "frost", "viper", "phoenix", "raven", "ghost",
            "wolf", "ninja", "storm", "fury", "blade", "saber", "havoc",
            "venom", "rebel", "rogue", "titan", "drake", "nova", "kage"
        };

        private static readonly string[] Separators = { "_", ".", "" };

        private static readonly Random Rng = new Random();

        /// <summary>Tek bir benzersiz isim uretir. Her cagrida farkli olma garantisi yok ama coklu cagrida pratikte cakisma az.</summary>
        public static string Generate()
        {
            int pattern = Rng.Next(0, 7);
            string result;

            switch (pattern)
            {
                case 0: // ahmetk04
                {
                    string name = Pick(TurkishNames);
                    char initial = (char)('a' + Rng.Next(26));
                    int num = Rng.Next(2, 1000);
                    result = name + initial + num;
                    break;
                }
                case 1: // jpinkman00
                {
                    char initial = (char)('a' + Rng.Next(26));
                    string name = Pick(ForeignNames);
                    int num = Rng.Next(0, 100);
                    result = initial + name + num.ToString("D2");
                    break;
                }
                case 2: // emre_07
                {
                    string name = Pick(TurkishNames);
                    string sep = Pick(Separators);
                    int num = Rng.Next(0, 100);
                    result = name + sep + num.ToString("D2");
                    break;
                }
                case 3: // selin2003 (year-like)
                {
                    string name = Rng.Next(0, 2) == 0 ? Pick(TurkishNames) : Pick(ForeignNames);
                    int year = Rng.Next(90, 110);
                    if (year >= 100) year -= 100; // 00-09 wrap
                    result = name + (year < 10 ? "0" + year : year.ToString());
                    if (Rng.Next(0, 3) == 0) result = name + (1990 + Rng.Next(0, 25)); // bazen tam yil
                    break;
                }
                case 4: // shadow99
                {
                    string tag = Pick(GamerTags);
                    int num = Rng.Next(1, 1000);
                    result = tag + num;
                    break;
                }
                case 5: // burak.acar
                {
                    string n1 = Pick(TurkishNames);
                    string n2 = Pick(TurkishNames);
                    result = n1 + "." + n2;
                    break;
                }
                case 6: // kk_lord99 / xx_phoenix_07
                {
                    char c1 = (char)('a' + Rng.Next(26));
                    char c2 = (char)('a' + Rng.Next(26));
                    string tag = Pick(GamerTags);
                    int num = Rng.Next(0, 100);
                    result = $"{c1}{c2}_{tag}{num.ToString("D2")}";
                    break;
                }
                default:
                    result = Pick(TurkishNames) + Rng.Next(10, 1000);
                    break;
            }

            // %30 ihtimal ilk harfi buyut (Ahmet, Mike99)
            if (Rng.Next(0, 10) < 3 && result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);

            return result;
        }

        /// <summary>N tane benzersiz isim uretir (cakisma kontrollu).</summary>
        public static string[] GenerateMany(int count)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            var sb = new System.Collections.Generic.List<string>(count);
            int safetyCounter = 0;
            while (sb.Count < count && safetyCounter < count * 10)
            {
                safetyCounter++;
                string n = Generate();
                if (seen.Add(n)) sb.Add(n);
            }
            // Yine de eksik kaldiysa (cok dusuk ihtimal) numarayla doldur
            while (sb.Count < count)
                sb.Add($"Player{Rng.Next(1000, 9999)}");
            return sb.ToArray();
        }

        private static string Pick(string[] arr) => arr[Rng.Next(arr.Length)];
    }
}
