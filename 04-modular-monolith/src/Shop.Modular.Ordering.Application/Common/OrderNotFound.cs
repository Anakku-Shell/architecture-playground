using Shop.Modular.BuildingBlocks;

namespace Shop.Modular.Ordering.Application.Common;

internal static class OrderNotFound
{
    public static NotFoundException For(Guid id) => new($"Order {id} does not exist.");
}
