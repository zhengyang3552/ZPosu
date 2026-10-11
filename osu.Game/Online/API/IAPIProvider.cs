// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Game.Localisation;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Notifications.WebSocket;

namespace osu.Game.Online.API
{
    public interface IAPIProvider
    {
        /// <summary>
        /// The local user.
        /// </summary>
        IBindable<APIUser> LocalUser { get; }

        /// <summary>
        /// The local user's current state.
        /// Contains auxiliary information such as the user's friends, blocks, and favourites,
        /// as well as methods to manage those in a way that keeps this state consistent throughout the game.
        /// </summary>
        ILocalUserState LocalUserState { get; }

        /// <summary>
        /// When there's ongoing SR/PP reprocessing, this will be non-empty and contain a URL leading to the news post
        /// giving user facing details about the ongoing deployment process.
        /// </summary>
        string ScoreProcessingNoticeUrl { get; }

        /// <summary>
        /// The language supplied by this provider to API requests.
        /// </summary>
        Language Language { get; }

        /// <summary>
        /// Retrieve the OAuth access token.
        /// </summary>
        string AccessToken { get; }

        /// <summary>
        /// Used as an identifier of a single local lazer session.
        /// Sent across the wire for the purposes of concurrency control to spectator server.
        /// </summary>
        Guid SessionIdentifier { get; }

        /// <summary>
        /// Returns whether the local user is logged in.
        /// </summary>
        bool IsLoggedIn { get; }

        /// <summary>
        /// Holds configuration for online endpoints.
        /// </summary>
        EndpointConfiguration Endpoints { get; }

        /// <summary>
        /// The version of the API.
        /// </summary>
        int APIVersion { get; }

        /// <summary>
        /// The current connection state of the API.
        /// This is not thread-safe and should be scheduled locally if consumed from a drawable component.
        /// </summary>
        IBindable<APIState> State { get; }

        /// <summary>
        /// When servers are on fire, this will contain a message which can be displayed to an end user.
        /// </summary>
        IBindable<string?> UserFacingOutageMessage { get; }

        /// <summary>
        /// Queue a new request.
        /// </summary>
        /// <param name="request">The request to perform.</param>
        void Queue(APIRequest request);

        /// <summary>
        /// Perform a request immediately, bypassing any API state checks.
        /// </summary>
        /// <remarks>
        /// Can be used to run requests as a guest user.
        /// </remarks>
        /// <param name="request">The request to perform.</param>
        void Perform(APIRequest request);

        /// <summary>
        /// Perform a request immediately, bypassing any API state checks.
        /// </summary>
        /// <remarks>
        /// Can be used to run requests as a guest user.
        /// </remarks>
        /// <param name="request">The request to perform.</param>
        Task PerformAsync(APIRequest request);

        /// <summary>
        /// Schedule a callback to run on the update thread.
        /// </summary>
        internal void Schedule(Action action);

        /// <summary>
        /// Constructs a new <see cref="IHubClientConnector"/>. May be null if not supported.
        /// </summary>
        /// <param name="clientName">The name of the client this connector connects for, used for logging.</param>
        /// <param name="endpoint">The endpoint to the hub.</param>
        IHubClientConnector? GetHubConnector(string clientName, string endpoint);

        /// <summary>
        /// Accesses the <see cref="INotificationsClient"/> used to receive asynchronous notifications from web.
        /// </summary>
        INotificationsClient NotificationsClient { get; }

        /// <summary>
        /// Creates a <see cref="IChatClient"/> instance to use in order to chat.
        /// </summary>
        IChatClient GetChatClient();
    }
}
