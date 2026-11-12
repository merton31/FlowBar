using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace FlowBar
{
    // Файлы тем: текстовый файл *.fbtheme в папке themes рядом с настройками. Без кода, только значения:
    //   // комментарий            $имя = значение — переменная, дальше пишется как $имя
    //   name = Закат              (обязательно)    author = Автор
    //   bg = #EE2B1E3E            fg = #FFE6E6FA   accent = #A490C2   blur = 0 | 3 | 4   (bg, fg, accent обязательны)
    //   material, material.tint, material.diffusion, material.edge, material.blur, opacity, gap, align, size, width — как в настройках вида
    sealed class FileTheme
    {
        public string Name, Author, File; public Sty Style; public Dictionary<string, string> Extras = new Dictionary<string, string>();
    }

    partial class Bar
    {
        static readonly Dictionary<string, FileTheme> fileThemes = new Dictionary<string, FileTheme>();
        static string ThemesDir { get { return F("themes"); } }
        static readonly string[] ThemeExtraKeys = { "material", "material.tint", "material.diffusion", "material.edge", "material.blur", "opacity", "gap", "align", "size", "width" };

        // разбор текста темы; при ошибке возвращает null и причину (строка файла — в тексте причины)
        internal static FileTheme ParseTheme(IEnumerable<string> lines, out string error)
        {
            error = null; var vars = new Dictionary<string, string>(); var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); int n = 0;
            foreach (var raw in lines)
            {
                n++; var line = raw.Trim(); if (line == "" || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { error = "строка " + n + ": нет «=»"; return null; }
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                int cm = val.IndexOf("//"); if (cm >= 0) val = val.Substring(0, cm).Trim(); // пояснение в конце строки
                if (val.StartsWith("$")) { string sub; if (!vars.TryGetValue(val, out sub)) { error = "строка " + n + ": переменная " + val + " не задана выше"; return null; } val = sub; }
                if (key.StartsWith("$")) vars[key] = val; else kv[key] = val;
            }
            string name; if (!kv.TryGetValue("name", out name) || PresetName(name) == "") { error = "нет строки name = …"; return null; }
            var t = new FileTheme { Name = PresetName(name) }; if (t.Name.Length > 32) t.Name = t.Name.Substring(0, 32).Trim();
            string a; t.Author = kv.TryGetValue("author", out a) ? a : "";
            string bg, fg, ac, bl;
            if (!kv.TryGetValue("bg", out bg) || !kv.TryGetValue("fg", out fg) || !kv.TryGetValue("accent", out ac)) { error = "нужны строки bg, fg и accent"; return null; }
            int blur = 0; if (kv.TryGetValue("blur", out bl) && (!int.TryParse(bl, out blur) || (blur != 0 && blur != 3 && blur != 4))) { error = "blur: 0, 3 или 4"; return null; }
            try { t.Style = new Sty(bg, fg, ac, blur); } catch (Exception) { error = "цвет не распознан: цвета пишутся как #RRGGBB или #AARRGGBB"; return null; }
            foreach (var key in ThemeExtraKeys)
            {
                string v; if (!kv.TryGetValue(key, out v)) continue; double d;
                bool ok = key == "material" ? MaterialKeys.Contains(v) : key == "material.tint" ? (v == "" || (v.Length == 7 && v[0] == '#'))
                    : key == "align" ? v == "left" || v == "center" || v == "right" : key == "size" ? v == "S" || v == "M" || v == "L" || v == "XL"
                    : key == "width" ? v == "fit" || v == "wide" || v == "full" : double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                if (!ok) { error = key + ": недопустимое значение «" + v + "»"; return null; }
                t.Extras[key] = v;
            }
            return t;
        }

        // перечитать папку тем: найденные темы попадают в общий список стилей под своим названием
        static void LoadThemeFiles()
        {
            foreach (var old in fileThemes.Keys) Styles.Remove(old);
            fileThemes.Clear();
            try
            {
                if (!Directory.Exists(ThemesDir)) return;
                foreach (var file in Directory.GetFiles(ThemesDir, "*.fbtheme").OrderBy(x => x))
                {
                    string err; FileTheme t;
                    try { t = ParseTheme(File.ReadAllLines(file, Encoding.UTF8), out err); } catch (Exception ex) { t = null; err = ex.Message; }
                    if (t == null) { Info("тема " + Path.GetFileName(file) + " не загружена: " + err); continue; }
                    if (Styles.ContainsKey(t.Name)) { Info("тема " + Path.GetFileName(file) + " пропущена: название «" + t.Name + "» уже занято"); continue; }
                    t.File = file; Styles[t.Name] = t.Style; fileThemes[t.Name] = t;
                }
            }
            catch (Exception ex) { Log(ex); }
        }

        // значения вида из файла темы ложатся в обычные настройки; остальное остаётся как было
        static void ApplyThemeExtras(string theme)
        {
            FileTheme t; if (!fileThemes.TryGetValue(theme, out t)) return;
            foreach (var kv in t.Extras) cfg[kv.Key] = kv.Value;
        }

        // первая папка тем: один файл-образец с пояснениями; он же рабочая тема
        static void EnsureThemesDir()
        {
            if (Directory.Exists(ThemesDir)) return;
            Directory.CreateDirectory(ThemesDir);
            File.WriteAllLines(Path.Combine(ThemesDir, "Пример темы.fbtheme"), new[] {
                "// Тема FlowBar. Строки, начатые с //, не читаются. Скопируйте файл, измените значения и откройте «Настройки → Оформление».",
                "// Цвета: #RRGGBB или #AARRGGBB (первые два знака — непрозрачность). Переменная: $имя = значение, дальше $имя.",
                "$main = #FF2B1E3E",
                "name = Пример темы",
                "author = FlowBar",
                "bg = $main              // фон панели",
                "fg = #FFE6E6FA          // текст и значки",
                "accent = #FFA490C2      // индикаторы, шкалы, кнопки",
                "blur = 0                // 0 — сплошная, 3 — размытие, 4 — акрил",
                "// Значения вида (необязательно): material = theme | frost | metal | prism; material.tint = #RRGGBB;",
                "// material.diffusion, material.edge = 0..100; material.blur = 0..32; opacity = 30..100; gap = 0..32;",
                "// align = left | center | right; size = S | M | L | XL; width = fit | wide | full",
                "opacity = 95",
                "gap = 12" }, new UTF8Encoding(true));
        }

        static string ColorText(Color c) { return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // текущий вид — в файл темы, чтобы можно было отправить другому человеку
        static string SaveThemeFile(string name)
        {
            name = PresetName(name); if (name == "") name = "Моя тема " + (fileThemes.Count + 1); if (name.Length > 32) name = name.Substring(0, 32).Trim();
            if (Styles.ContainsKey(name) && !fileThemes.ContainsKey(name)) name += " (своя)";
            var s = Cur; var lines = new List<string> { "// Тема FlowBar. Цвета: #RRGGBB или #AARRGGBB. Строки со // не читаются.", "name = " + name, "author = ",
                "bg = " + ColorText(s.Bg), "fg = " + ColorText(s.Fg), "accent = " + ColorText(AccentColor), "blur = " + s.Blur };
            foreach (var key in ThemeExtraKeys) { string v; if (cfg.TryGetValue(key, out v)) lines.Add(key + " = " + v); }
            Directory.CreateDirectory(ThemesDir);
            string file = Path.Combine(ThemesDir, new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()) + ".fbtheme");
            File.WriteAllLines(file, lines, new UTF8Encoding(true));
            LoadThemeFiles();
            return name;
        }
    }
}
