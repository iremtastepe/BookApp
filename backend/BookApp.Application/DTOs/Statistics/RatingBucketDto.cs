namespace BookApp.Application.DTOs.Statistics;

// Puan dağılımının bir çubuğu: "4.5 puan verilen kitap sayısı".
// Puan 1.0 ile 5.0 arasında 0.5 adımlı olduğu için toplam 9 çubuk (kova) olacak.
public class RatingBucketDto
{
    public decimal Rating { get; set; }
    public int Count { get; set; }
}
