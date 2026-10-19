using System.Globalization;
using BookApp.Application.DTOs.Statistics;
using BookApp.Application.Helpers;
using BookApp.Application.Interfaces;
using BookApp.Domain.Enums;

namespace BookApp.Application.Services;

public class StatisticsService : IStatisticsService
{
    // Genel özetteki "en çok okunan 5 ..." ve "en yüksek puanlı 5 kitap" listelerinin uzunluğu
    private const int TopCount = 5;

    // Dış kitap servisleri yazarı bulamayınca bu yer tutucuyu yazıyor; istatistikte yazar sayılmaz
    private const string UnknownAuthor = "Bilinmeyen Yazar";

    // Türkçe kültür: "Işık" ile "ışık", "İstanbul" ile "istanbul" aynı sayılsın ve sıralama Türkçe alfabeye göre olsun
    private static readonly CultureInfo TurkishCulture = new("tr-TR");
    private static readonly StringComparer TurkishIgnoreCase = StringComparer.Create(TurkishCulture, ignoreCase: true);
    private static readonly StringComparer TurkishOrder = StringComparer.Create(TurkishCulture, ignoreCase: false);

    private readonly IStatisticsRepository _statisticsRepository;

    public StatisticsService(IStatisticsRepository statisticsRepository)
    {
        _statisticsRepository = statisticsRepository;
    }

    public async Task<OverviewStatisticsDto> GetOverviewAsync(int userId)
    {
        // Sorgular sırayla çalışır: tek DbContext aynı anda birden fazla sorguyu kaldırmaz
        var statusCounts = await _statisticsRepository.GetStatusCountsAsync(userId);
        var favoriteCount = await _statisticsRepository.GetFavoriteCountAsync(userId);
        var authorCounts = await _statisticsRepository.GetReadAuthorCountsAsync(userId);
        var genreCounts = await _statisticsRepository.GetReadGenreCountsAsync(userId);
        var genreMissingCount = await _statisticsRepository.GetReadGenreMissingCountAsync(userId);
        var topRatedBooks = await _statisticsRepository.GetTopRatedReadBooksAsync(userId, TopCount);
        var ratingCounts = await _statisticsRepository.GetReadRatingCountsAsync(userId);

        return new OverviewStatisticsDto
        {
            TotalBooks = statusCounts.Values.Sum(),
            StatusCounts = BuildStatusCounts(statusCounts),
            FavoriteCount = favoriteCount,
            // Birleştirilmiş liste zaten "sayı azalan, sonra ad" sırasında; 5. ve 6. eşitse alfabetik ilk 5 kalır
            TopAuthors = MergeNames(authorCounts, isAuthor: true).Take(TopCount).ToList(),
            TopGenres = MergeNames(genreCounts, isAuthor: false).Take(TopCount).ToList(),
            GenreMissingCount = genreMissingCount,
            TopRatedBooks = topRatedBooks,
            RatingDistribution = BuildRatingDistribution(ratingCounts)
        };
    }

    public async Task<MonthlyStatisticsDto> GetMonthlyAsync(int userId, int year, int month)
    {
        // Ortak yıl/ay kuralları (DateValidator): geçersizse ArgumentException -> controller 400 döner
        DateValidator.ValidateYearRange(year);
        DateValidator.ValidateMonth(month);

        var completedCount = await _statisticsRepository.GetReadCountAsync(userId, year, month);
        var authorCounts = await _statisticsRepository.GetReadAuthorCountsAsync(userId, year, month);
        var genreCounts = await _statisticsRepository.GetReadGenreCountsAsync(userId, year, month);
        var ratedBooks = await _statisticsRepository.GetRatedReadBooksAsync(userId, year, month);

        var highestRatedBooks = new List<RatedBookDto>();
        var lowestRatedBooks = new List<RatedBookDto>();

        // Puanlı kitap yoksa iki liste de boş kalır; varsa en yüksek/en düşük puanı paylaşanların HEPSİ alınır
        if (ratedBooks.Count > 0)
        {
            var highestRating = ratedBooks.Max(b => b.Rating);
            var lowestRating = ratedBooks.Min(b => b.Rating);

            highestRatedBooks = SortRatedBooks(ratedBooks.Where(b => b.Rating == highestRating));
            lowestRatedBooks = SortRatedBooks(ratedBooks.Where(b => b.Rating == lowestRating));
        }

        return new MonthlyStatisticsDto
        {
            Year = year,
            Month = month,
            CompletedCount = completedCount,
            TopAuthors = TakeLeaders(MergeNames(authorCounts, isAuthor: true)),
            TopGenres = TakeLeaders(MergeNames(genreCounts, isAuthor: false)),
            HighestRatedBooks = highestRatedBooks,
            LowestRatedBooks = lowestRatedBooks
        };
    }

    public async Task<YearlyStatisticsDto> GetYearlyAsync(int userId, int year)
    {
        // Ortak yıl kuralı (DateValidator): geçersizse ArgumentException -> controller 400 döner
        DateValidator.ValidateYearRange(year);

        var yearCounts = await _statisticsRepository.GetReadCountsByYearAsync(userId);
        var monthCounts = await _statisticsRepository.GetReadCountsByMonthAsync(userId, year);
        var unknownMonthCount = await _statisticsRepository.GetReadUnknownMonthCountAsync(userId, year);
        var undatedReadCount = await _statisticsRepository.GetUndatedReadCountAsync(userId);

        // Veritabanı sadece kitabı olan ayları döndürür; 12 ayın hepsini sıfırlarla biz tamamlarız
        var monthLookup = monthCounts.ToDictionary(x => x.Month, x => x.Count);
        var months = Enumerable.Range(1, 12)
            .Select(m => new MonthCountDto { Month = m, Count = monthLookup.GetValueOrDefault(m) })
            .ToList();

        return new YearlyStatisticsDto
        {
            Year = year,
            Years = yearCounts.OrderBy(y => y.Year).ToList(),
            Months = months,
            UnknownMonthCount = unknownMonthCount,
            UndatedReadCount = undatedReadCount
        };
    }

    // ---- Yardımcı metotlar ----

    // Veritabanı yalnızca kitabı olan durumları döndürür; 5 durumun hepsini 0'larla tamamlarız
    private static List<StatusCountDto> BuildStatusCounts(Dictionary<ReadingStatus, int> counts)
        => Enum.GetValues<ReadingStatus>()
            .Select(status => new StatusCountDto
            {
                Status = status,
                StatusName = status.ToString(),
                Count = counts.GetValueOrDefault(status)
            })
            .ToList();

    // Puan 1.0 - 5.0 arası 0.5 adımlı olduğu için 9 kova vardır; kitabı olmayan kovalar 0 ile gelir
    private static List<RatingBucketDto> BuildRatingDistribution(List<RatingBucketDto> counts)
    {
        var lookup = counts.ToDictionary(x => x.Rating, x => x.Count);

        // i = 2..10 -> i / 2 = 1.0, 1.5, ... 5.0
        return Enumerable.Range(2, 9)
            .Select(i =>
            {
                var rating = i / 2m;
                return new RatingBucketDto { Rating = rating, Count = lookup.GetValueOrDefault(rating) };
            })
            .ToList();
    }

    // Ham yazar/tür sayılarını temizler ve birleştirir:
    //  - boşluklar kırpılır, boş adlar atılır
    //  - yazarlarda "Bilinmeyen Yazar" yer tutucusu atılır
    //  - büyük/küçük harf farkı olan adlar tek grupta toplanır (Türkçe kurallarıyla)
    //  - görünen ad, grupta en çok geçen yazılıştır (eşitse alfabetik ilki)
    //  - sonuç: sayı azalan, sonra ad artan (her istekte aynı sıra)
    private static List<NameCountDto> MergeNames(IEnumerable<NameCountDto> rawCounts, bool isAuthor)
    {
        var cleaned = rawCounts
            .Select(x => new NameCountDto { Name = x.Name.Trim(), Count = x.Count })
            .Where(x => x.Name.Length > 0);

        if (isAuthor)
            cleaned = cleaned.Where(x => !TurkishIgnoreCase.Equals(x.Name, UnknownAuthor));

        return cleaned
            .GroupBy(x => x.Name, TurkishIgnoreCase)
            .Select(g => new NameCountDto
            {
                Name = g.OrderByDescending(x => x.Count).ThenBy(x => x.Name, TurkishOrder).First().Name,
                Count = g.Sum(x => x.Count)
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, TurkishOrder)
            .ToList();
    }

    // Sıralı listeden en yüksek sayıya sahip olanların HEPSİNİ alır (aylık liderler)
    private static List<NameCountDto> TakeLeaders(List<NameCountDto> mergedAndSorted)
    {
        if (mergedAndSorted.Count == 0)
            return new List<NameCountDto>();

        var highestCount = mergedAndSorted.Max(x => x.Count);
        return mergedAndSorted.Where(x => x.Count == highestCount).ToList();
    }

    // Aynı puanlı kitaplar için sabit sıra: günü bilinenler önce (yeni gün önce), sonra kitap adı
    private static List<RatedBookDto> SortRatedBooks(IEnumerable<RatedBookDto> books)
        => books
            .OrderBy(b => b.FinishedDay == null)
            .ThenByDescending(b => b.FinishedDay)
            .ThenBy(b => b.Title, TurkishOrder)
            .ThenBy(b => b.UserBookId)
            .ToList();
}