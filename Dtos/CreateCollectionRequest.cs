using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Dtos;

public class CreateCollectionRequest
{
    [Required]
    [StringLength(80, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    public int OwnerId { get; set; }
}