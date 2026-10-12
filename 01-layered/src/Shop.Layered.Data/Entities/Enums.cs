namespace Shop.Layered.Data.Entities;

// The enums live next to the entities because the entities are the model of this version.
// They are stored as strings (see ShopDbContext) and serialised as strings by the Api, so
// renaming a member changes both the database and the public JSON: one more coupling of 01.

/// <summary>Where an order is in its lifecycle (Guide §3.11).</summary>
public enum OrderStatus
{
    AwaitingPayment,
    Rejected,
    Paid,
    Cancelled,
}

/// <summary>Why an order was cancelled.</summary>
public enum CancellationReason
{
    CustomerCancelled,
    PaymentDeclined,
}

/// <summary>What the payment gateway answered.</summary>
public enum PaymentStatus
{
    Approved,
    Declined,
}
