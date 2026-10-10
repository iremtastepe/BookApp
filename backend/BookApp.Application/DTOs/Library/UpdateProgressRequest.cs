using System.Text.Json.Serialization;

namespace BookApp.Application.DTOs.Library;

public class UpdateProgressRequest
{
    // [JsonRequired]: "currentPage" alanı JSON içinde MUTLAKA bulunmalı. Böylece iki durum ayrılır:
    //   {"currentPage": 120}  -> ilerleme güncellenir
    //   {"currentPage": null} -> ilerleme açıkça temizlenir (alan var, değeri null)
    //   {}                    -> alan hiç yok, istek 400 ile reddedilir (yanlışlıkla ilerleme silinmesin)
    // Negatif değer ve kitabın toplam sayfasını aşan değer serviste doğrulanır (toplam sayfa
    // Book'ta olduğu için burada, tek başına bu sınıfta kontrol edilemez).
    [JsonRequired]
    public int? CurrentPage { get; set; }
}