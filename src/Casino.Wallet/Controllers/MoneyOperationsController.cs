using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Wallet.Controllers;

[ApiController]
public sealed class MoneyOperationsController(WalletStore store) : ControllerBase
{
    [HttpPost("/deposits")]
    public ActionResult<Operation> Deposit([FromBody] MoneyRequest request) => Ok(store.Change(request, "deposit"));

    [HttpPost("/withdrawals")]
    public ActionResult<Operation> Withdraw([FromBody] MoneyRequest request) => Ok(store.Change(request, "withdraw"));
}
