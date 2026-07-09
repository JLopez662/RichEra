using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

// Parses an OFX 1.x (SGML) bank statement export into a neutral ParsedStatement.
// Tolerant of unclosed leaf tags, which BPPR's "Microsoft Money" export uses.
public interface IOfxParser
{
    ParsedStatement Parse(Stream stream);
}
