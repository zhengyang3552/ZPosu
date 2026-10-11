// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Notifications.WebSocket;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Online.API
{
    public partial class APIAccess : CompositeComponent, IAPIProvider
    {
        private readonly OsuGameBase game;

        private readonly string versionHash;

        private readonly Queue<APIRequest> queue = new Queue<APIRequest>();

        public EndpointConfiguration Endpoints { get; }

        /// <summary>
        /// The API response version.
        /// See: https://osu.ppy.sh/docs/index.html#api-versions
        /// </summary>
        public int APIVersion { get; }

        public string ScoreProcessingNoticeUrl { get; private set; } = string.Empty;

        public IBindable<APIUser> LocalUser => localUserState.User;

        public ILocalUserState LocalUserState => localUserState;
        private readonly LocalUserState localUserState;

        public INotificationsClient NotificationsClient { get; }

        public Language Language => game.CurrentLanguage.Value;

        /// <summary>
        /// Always <c>false</c>. This build runs in permanent guest mode and never authenticates.
        /// </summary>
        protected bool HasLogin => false;

        private readonly CancellationTokenSource cancellationToken = new CancellationTokenSource();

        [CanBeNull]
        public Action<Notification> PostNotification { get; set; }

        public APIAccess(OsuGameBase game, OsuConfigManager config, EndpointConfiguration endpoints, string versionHash)
        {
            this.game = game;
            this.versionHash = versionHash;

            if (game.IsDeployedBuild)
                APIVersion = game.AssemblyVersion.Major * 10000 + game.AssemblyVersion.Minor;
            else
            {
                var now = DateTimeOffset.Now;
                APIVersion = now.Year * 10000 + now.Month * 100 + now.Day;
            }

            Endpoints = endpoints;
            NotificationsClient = setUpNotificationsClient();

            // Account credentials are never read from configuration in this build; guest mode is forced.
            // Any credentials left over from a previous run are simply ignored.
            AddInternal(localUserState = new LocalUserState(this, config));

            // Permanent guest mode: never attempt to connect, and never leave the offline state.
            state.Value = APIState.Offline;

            var thread = new Thread(run)
            {
                Name = "APIAccess",
                IsBackground = true
            };

            state.BindValueChanged(val =>
            {
                // assume that any change from a failing state means that the previously set outage message is no longer valid.
                if (val.NewValue != APIState.Failing)
                    userFacingOutageMessage.Value = null;
            });

            thread.Start();
        }

        private WebSocketNotificationsClientConnector setUpNotificationsClient()
        {
            var connector = new WebSocketNotificationsClientConnector(this);

            connector.MessageReceived += msg =>
            {
                switch (msg.Event)
                {
                    case @"verified":
                        break;

                    case @"logout":
                        // Guest mode: there is no session that can be logged out.
                        break;
                }
            };

            return connector;
        }

        void IAPIProvider.Schedule(Action action) => base.Schedule(action);

        /// <summary>
        /// Always <c>null</c>. This build runs in permanent guest mode and never holds a session token.
        /// </summary>
        public string AccessToken => null;

        public Guid SessionIdentifier { get; } = Guid.NewGuid();

        /// <summary>
        /// The main API thread loop, which will continue to run until the game is shut down.
        /// In permanent guest mode this thread simply parks the API in <see cref="APIState.Offline"/>
        /// and fails any queued requests, without ever contacting the server.
        /// </summary>
        private void run()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Guest mode: there are never credentials, so the API stays offline and any
                // queued request is failed by `Queue()` rather than being sent.
                state.Value = APIState.Offline;
                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Never connects. This build runs in permanent guest mode: there are no credentials to
        /// authenticate with, and the connection thread stays in <see cref="APIState.Offline"/>.
        /// </summary>
        private void attemptConnect()
        {
        }

        public void Perform(APIRequest request)
        {
            try
            {
                request.AttachAPI(this);
                request.Perform();
            }
            catch (Exception e)
            {
                // todo: fix exception handling
                request.Fail(e);
            }
        }

        public Task PerformAsync(APIRequest request) =>
            Task.Factory.StartNew(() => Perform(request), TaskCreationOptions.LongRunning);

        public IHubClientConnector GetHubConnector(string clientName, string endpoint) =>
            new HubClientConnector(clientName, endpoint, this, versionHash);

        public IChatClient GetChatClient() => new WebSocketChatClient(this);

        /// <summary>
        /// Handle a single API request.
        /// Ensures all exceptions are caught and dealt with correctly.
        /// </summary>
        /// <param name="req">The request.</param>
        /// <returns>true if the request succeeded.</returns>
        private bool handleRequest(APIRequest req)
        {
            try
            {
                req.AttachAPI(this);
                req.Perform();

                if (req.CompletionState != APIRequestCompletionState.Completed)
                    return false;

                return true;
            }
            catch (HttpRequestException re)
            {
                Logger.Log($"{nameof(HttpRequestException)} while performing request {req}: {re.Message}", LoggingTarget.Network);
                handleFailure();
                return false;
            }
            catch (SocketException se)
            {
                Logger.Log($"{nameof(SocketException)} while performing request {req}: {se.Message}", LoggingTarget.Network);
                handleFailure();
                return false;
            }
            catch (WebException we)
            {
                Logger.Log($"{nameof(WebException)} while performing request {req}: {we.Message}", LoggingTarget.Network);
                handleWebException(we);
                return false;
            }
            catch (WebRequestFlushedException wrf)
            {
                Logger.Log(wrf.Message, LoggingTarget.Network);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error occurred while handling an API request.");
                return false;
            }
        }

        private readonly Bindable<APIState> state = new Bindable<APIState>();

        /// <summary>
        /// The current connectivity state of the API.
        /// </summary>
        public IBindable<APIState> State => state;

        public IBindable<string> UserFacingOutageMessage => userFacingOutageMessage;

        private readonly Bindable<string> userFacingOutageMessage = new Bindable<string>(string.Empty);

        private void handleWebException(WebException we)
        {
            HttpStatusCode statusCode = (we.Response as HttpWebResponse)?.StatusCode
                                        ?? (we.Status == WebExceptionStatus.UnknownError ? HttpStatusCode.NotAcceptable : HttpStatusCode.RequestTimeout);

            // special cases for un-typed but useful message responses.
            switch (we.Message)
            {
                case "Unauthorized":
                case "Forbidden":
                    statusCode = HttpStatusCode.Unauthorized;
                    break;
            }

            switch (statusCode)
            {
                case HttpStatusCode.Unauthorized:
                    Logout();
                    break;

                case HttpStatusCode.RequestTimeout:
                    handleFailure();
                    break;
            }
        }

        private void handleFailure()
        {
        }

        /// <summary>
        /// Always <c>false</c>. This build runs in permanent guest mode.
        /// </summary>
        public bool IsLoggedIn => false;

        public void Queue(APIRequest request)
        {
            lock (queue)
            {
                request.AttachAPI(this);

                if (state.Value == APIState.Offline)
                {
                    request.Fail(new WebException(@"User not logged in"));
                    return;
                }

                queue.Enqueue(request);
            }
        }

        private void flushQueue(bool failOldRequests = true)
        {
            lock (queue)
            {
                var oldQueueRequests = queue.ToArray();

                queue.Clear();

                if (failOldRequests)
                {
                    foreach (var req in oldQueueRequests)
                        req.Fail(new WebRequestFlushedException(state.Value));
                }
            }
        }

        /// <summary>
        /// No-op. This build runs in permanent guest mode, so there is never a session to end.
        /// The local user is held in guest state (see <see cref="LocalUserState"/>), which the API
        /// connection thread relies on to remain in <see cref="APIState.Offline"/>.
        /// </summary>
        public void Logout()
        {
            flushQueue();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            flushQueue();
            cancellationToken.Cancel();
        }

        internal class WebRequestFlushedException : Exception
        {
            public WebRequestFlushedException(APIState state)
                : base($@"Request failed from flush operation (state {state})")
            {
            }
        }
    }

    internal class GuestUser : APIUser
    {
        public GuestUser()
        {
            Username = @"Guest";
            Id = SYSTEM_USER_ID;
        }
    }

    public enum APIState
    {
        /// <summary>
        /// We cannot login (not enough credentials).
        /// </summary>
        Offline,

        /// <summary>
        /// We are having connectivity issues.
        /// </summary>
        Failing,

        /// <summary>
        /// Waiting on second factor authentication.
        /// </summary>
        RequiresSecondFactorAuth,

        /// <summary>
        /// We are in the process of (re-)connecting.
        /// </summary>
        Connecting,

        /// <summary>
        /// We are online.
        /// </summary>
        Online
    }
}
