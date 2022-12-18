using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace TPaperDelivery
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeliveryController
    {
        public DeliveryController()
        {
        }

        [HttpGet]
        [Route("create/{clientId}/{ediOrderId}/{productCode}/{number}")]
        public async Task<IActionResult> ProcessEdiOrder(
            int clientId,
            int ediOrderId,
            int productCode,
            int number,
            CancellationToken cts)
        {
            var newDelivery = new Delivery
            {
                Id = 0,
                ClientId = clientId,
                EdiOrderId = ediOrderId,
                Number = number,
                ProductId = 1,
                ProductCode = 33,
                Notes = "Prepared for shipment"
            };

            return new OkObjectResult(newDelivery);
        }

        [HttpGet]
        [Route("get")]
        public async Task<IActionResult> Get(CancellationToken cts)
        {
            return new OkObjectResult("Started");
        }
    }
}
