using System;
using System.Net;
using EntityGraphQL;
using EntityGraphQL.Schema;
using Microsoft.AspNetCore.Mvc;

namespace demo.Controllers
{
    [Route("api/[controller]")]
    public class QueryController : Controller
    {
        private readonly DemoContext _dbContext;
        private readonly SchemaProvider<DemoContext> _schemaProvider;

        public QueryController(DemoContext dbContext, SchemaProvider<DemoContext> schemaProvider)
        {
            this._dbContext = dbContext;
            this._schemaProvider = schemaProvider;
        }

        [HttpGet]
        public object Get([FromQuery] string query)
        {
            return RunDataQuery(new QueryRequest { Query = query });
        }

        [HttpPost]
        public object Post([FromBody] QueryRequest query)
        {
            return RunDataQuery(query);
        }

        private object RunDataQuery(QueryRequest query)
        {
            try
            {
                // _serviceProvider is passed to resolve dependencies in mutations and field selections at run time which opens a lot of flexibility
                // last argument can be claims to implement security checks
                var data = _schemaProvider.ExecuteQuery(query, _dbContext, HttpContext.RequestServices, null);
                return data;
            }
            catch (Exception)
            {
                return HttpStatusCode.InternalServerError;
            }
        }
    }
}