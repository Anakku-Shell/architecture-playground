namespace Shop.Micro.Contracts;

/// <summary>
/// Marks a message that travels between services through the broker. Two kinds use it (Guide §8.4):
/// a <b>command</b> asks one service to do something (<c>ReserveStock</c>: imperative name, exactly one
/// receiver) and an <b>event</b> states that something happened (<c>StockReserved</c>: past tense, the
/// sender does not care who listens). The type's name is its routing key, so renaming one is a breaking change.
/// </summary>
public interface IIntegrationMessage;
