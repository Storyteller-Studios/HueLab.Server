using System.ComponentModel.DataAnnotations;

namespace HueLab.Server.Models.DTO.Requests;

public sealed record SubmitColorRequest(
    [Required, MinLength(4), MaxLength(4)] IReadOnlyList<string> Colors);
