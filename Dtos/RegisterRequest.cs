using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Dtos;

public class RegisterRequest
{
    [Required]
    [EmailAddress]
    [StringLength(320, MinimumLength = 1)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
