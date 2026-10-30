// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Messaging;

/// <summary>
/// A transport's way of saying that a message was refused for a reason sending it again cannot
/// change: the address does not exist, or is not an address at all.
/// </summary>
/// <remarks>
/// Thrown by a sender and turned into <see cref="MessageResult.Permanent"/> by the dispatcher, so
/// the code that schedules retries never has to know what a mail server's status codes mean. A
/// failure that is not this — a server that is down, a refused connection, a temporary
/// "try later" answer — stays an ordinary failure and is retried on the usual ladder.
/// </remarks>
public sealed class PermanentDeliveryException : Exception
{
    public PermanentDeliveryException(string message)
        : base(message)
    {
    }

    public PermanentDeliveryException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
