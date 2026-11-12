using Casino.Wallet.Wallets;
using Casino.Wallet.Persistence.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Wallet.Controllers;

[ApiController]
public sealed class MoneyOperationsController(WalletService store) : ControllerBase
{
    [HttpPost("/deposits")]
    public async Task<ActionResult<Operation>> Deposit([FromBody] MoneyRequest request, CancellationToken ct) => Ok(await store.Change(request, MoneyOperationKind.Deposit, ct));

    [HttpPost("/withdrawals")]
    public async Task<ActionResult<Operation>> Withdraw([FromBody] MoneyRequest request, CancellationToken ct) => Ok(await store.Change(request, MoneyOperationKind.Withdraw, ct));
}
