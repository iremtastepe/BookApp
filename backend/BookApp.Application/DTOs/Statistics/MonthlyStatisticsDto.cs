using System.Collections.Generic;

namespace BookApp.Application.DTOs.Statistics;

// GET /api/Statistics/monthly?year=2026&month=10 cevabı
// Hesap "tamamlanma tarihine" göre yapılır, puan verilme tarihine göre değil.
public class MonthlyStatisticsDto
{
    public int Year { get; set; }
    public int Month { get; set; }

    // Seçilen ayda tamamlanan (Read) kitap sayısı.
    // Ayda kitap olup olmadığını BU alan belirler; aşağıdaki listelerin boş olması kitap yok demek değildir.
    public int CompletedCount { get; set; }

    // Eşit liderlerin HEPSİ döner (örn. 3 farklı yazardan birer kitap varsa üçü de listede).
    // Liste boşsa bu ayda yazar/tür bilgisi olan kitap yok demektir: kitaplarda yazar veya tür
    // bilgisi eksik olabilir (örn. "Bilinmeyen Yazar" sayılmaz, türü boş kitaplar sayılmaz).
    // Her listedeki Count değeri aynıdır.
    public List<NameCountDto> TopAuthors { get; set; } = new();
    public List<NameCountDto> TopGenres { get; set; } = new();

    // En yüksek/en düşük puanı paylaşan kitapların hepsi döner. Puanlı kitap yoksa liste boştur.
    // Tek puanlı bir ayda iki liste de aynı kitabı içerir, frontend isterse birini gizler.
    public List<RatedBookDto> HighestRatedBooks { get; set; } = new();
    public List<RatedBookDto> LowestRatedBooks { get; set; } = new();
}
