using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LumaDock;

// Проверка поиска городов для погоды по настоящему интернету (--check-places <папка>): русские названия находятся при любом языке интерфейса,
// область и страна различают одноимённые города. Результат в result.txt, успешный заканчивается строкой OK. Не входит в --smoke, чтобы он не зависел от сети.
internal static class PlaceCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new StringBuilder();
        try
        {
            foreach (var (language, query, expected, country) in new[]
            {
                ("ru", "Москва", "Москва", "Россия"), ("ru", "Санкт-Петербург", "Санкт-Петербург", "Россия"), ("ru", "Новосибирск", "Новосибирск", "Россия"),
                ("en", "Москва", "Москва", "Россия"), ("en", "Moscow", "Moscow", "Russia")
            })
            {
                L10n.SetLanguage(language);
                var places = LiveData.FindPlaces(query).GetAwaiter().GetResult();
                var match = places.FirstOrDefault(place => place.Name.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && place.Region.Contains(country, StringComparison.OrdinalIgnoreCase));
                log.AppendLine($"{language} «{query}»: " + string.Join("; ", places.Take(4).Select(place => place.Name + " (" + place.Region + ")")));
                if (match is null) throw new InvalidOperationException($"«{query}» was not found with the {language} interface.");
            }
            log.AppendLine("OK");
            File.WriteAllText(Path.Combine(folder, "result.txt"), log.ToString(), new UTF8Encoding(false));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "result.txt"), log.ToString() + ex, new UTF8Encoding(false));
            return 1;
        }
    }
}
