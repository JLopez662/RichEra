namespace RichEra.Application.Dtos;

// An item's picture, carried as raw bytes plus a ready-to-render data: URI. The UI binds
// <img src="@dto.DataUri"> directly — no file path, no extra round-trip.
public sealed record ItemImageDto(string Kind, Guid OwnerId, string ContentType, byte[] Data)
{
    public string DataUri => $"data:{ContentType};base64,{Convert.ToBase64String(Data)}";
}
