﻿using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace AspNetCore.Security.CAS
{
    internal class CasHandler : RemoteAuthenticationHandler<CasOptions>
    {
        /// <summary>
        /// The handler calls methods on the events which give the application control at certain points where processing is occurring.
        /// If it is not provided a default instance is supplied which does nothing when the methods are called.
        /// </summary>
        protected new CasEvents Events
        {
            get => (CasEvents)base.Events;
            set => base.Events = value;
        }

        public CasHandler(IOptionsMonitor<CasOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task<object> CreateEventsAsync() => Task.FromResult<object>(new CasEvents());

        protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
        {
            AuthenticationProperties properties = null;
            var query = Request.Query;
            var state = query["state"];

            properties = Options.StateDataFormat.Unprotect(state);
            if (properties == null)
            {
                return HandleRequestResult.Fail("The state was missing or invalid.");
            }
            
            // OAuth2 10.12 CSRF
            if (!ValidateCorrelationId(properties))
            {
                return HandleRequestResult.Fail("Correlation failed.");
            }
            
            var casTicket = query["ticket"];
            if (string.IsNullOrEmpty(casTicket))
            {
                return HandleRequestResult.Fail("Missing CAS ticket.");
            }
            
            var casService = Uri.EscapeDataString(BuildReturnTo(state));
            var authTicket = await Options.TicketValidator.ValidateTicket(Context, properties, Scheme, Options, casTicket, casService);
            if (authTicket == null)
            {
                return HandleRequestResult.Fail("Failed to retrieve user information from remote server.");
            }
            
            return HandleRequestResult.Success(authTicket);
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (string.IsNullOrEmpty(properties.RedirectUri))
            {
                properties.RedirectUri = CurrentUri;
            }

            // OAuth2 10.12 CSRF
            GenerateCorrelationId(properties);

            var returnTo = BuildReturnTo(Options.StateDataFormat.Protect(properties));
            var authorizationEndpoint = $"{Options.CasServerUrlBase}/login?service={Uri.EscapeDataString(returnTo)}";
            var redirectContext = new RedirectContext<CasOptions>(Context, Scheme, Options, properties, authorizationEndpoint);

            await Options.Events.RedirectToAuthorizationEndpoint(redirectContext);
        }

        private string BuildReturnTo(string state)
        {
            return Request.Scheme + "://" + Request.Host +
                   Request.PathBase + Options.CallbackPath +
                   "?state=" + Uri.EscapeDataString(state);
        }
    }
}