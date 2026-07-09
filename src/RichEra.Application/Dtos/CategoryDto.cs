using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

public record CategoryDto(
    Guid Id,
    string Name,
    CategoryKind Kind,
    string? Icon,
    string? ColorHex,
    bool IsArchived);

public record CategoryUpsertDto(
    Guid? Id,
    string Name,
    CategoryKind Kind,
    string? Icon,
    string? ColorHex);
