// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Tests
{
    /// <summary>
    /// Helpers for substituting the local user in tests without a login flow.
    /// </summary>
    /// <remarks>
    /// The production build has no account system and always runs as a guest, so tests that need a
    /// populated local user use this to inject one directly rather than logging in.
    /// </remarks>
    public static class DummyAPIAccessExtensions
    {
        /// <summary>
        /// Populate the local user of a <see cref="DummyAPIAccess"/> with a fake user and put it online.
        /// </summary>
        public static void LoginAsDummyUser(this DummyAPIAccess api, string username = @"Local user", int id = DummyAPIAccess.DUMMY_USER_ID)
        {
            api.LocalUserState.SetUser(new APIUser
            {
                Username = username,
                Id = id,
            });

            api.SetState(APIState.Online);
        }

        /// <summary>
        /// Reset the local user of a <see cref="DummyAPIAccess"/> to a guest and take it offline.
        /// </summary>
        public static void LogoutDummyUser(this DummyAPIAccess api)
        {
            api.LocalUserState.SetUser(new GuestUser());
            api.SetState(APIState.Offline);
        }
    }
}
