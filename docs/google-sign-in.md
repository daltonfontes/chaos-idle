# Google sign-in: setup

The game, the server and CI are ready. What's left is creating the Google Cloud credentials and
giving their values to the server and to the Android build. Nothing here goes into the
repository except two Actions variables.

## How it fits together

1. In the Android app, the **Account** tab's "Sign in with Google" calls the plugin
   (`android/google-sign-in`). The plugin uses Android's Credential Manager and shows Google's
   account sheet.
2. Google returns an **ID token** for our **Web** client id.
3. The game sends the token to the server (`POST /v1/auth/google` or
   `/v1/account/link/google`). The server checks the signature, issuer, audience (the Web client
   id) and expiry (`docs/backend.md`, "Sign-in").

Android only gives the app a token if an **Android** OAuth client exists in the same Google
Cloud project, with the app's package name and the **SHA-1 of the key that signed the APK**.

## Steps

1. **Google Cloud project.** Open the Google Cloud console and create a project (for example
   "Chaos Idle").
2. **Consent screen.** In "APIs & Services", then "OAuth consent screen":
   - User type: External.
   - App name, support email.
   - Scopes: `openid`, `email`, `profile`.
   - While in Testing, add your Google account as a test user.
3. **Web client.** In "Credentials", choose "Create credentials", then "OAuth client ID", then
   "Web application". Copy its **client id** (`…apps.googleusercontent.com`). It is used twice:
   - by the server: `Auth__Google__ClientIds__0=<web client id>`;
   - by the APK: the repository variable `GOOGLE_WEB_CLIENT_ID` (step 6).
4. **A stable signing key for the APK.** CI makes a new debug key on every run unless you give
   it one, and Google ties the Android client to one key. Make one once:

   ```bash
   keytool -genkeypair -v -keystore debug.keystore -storepass android -alias androiddebugkey -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Android Debug,O=Android,C=US"
   ```

   ```bash
   base64 -w0 debug.keystore
   ```

   Save the base64 text as the repository **secret** `ANDROID_DEBUG_KEYSTORE_BASE64`
   (Settings > Secrets and variables > Actions > Secrets).
5. **Android client.** In "Credentials", choose "Create credentials", then "OAuth client ID",
   then "Android":
   - package name `com.chaosidle.game`;
   - SHA-1 of the key from step 4. The "Godot client" CI log prints it under "Signing key
     SHA-1", or run:

     ```bash
     keytool -list -v -keystore debug.keystore -storepass android -alias androiddebugkey
     ```

   This client has no id to copy: it only lets the app ask for tokens.
6. **Repository variables** (Settings > Secrets and variables > Actions > Variables):
   - `GOOGLE_WEB_CLIENT_ID`: the Web client id from step 3.
   - `CHAOSIDLE_SERVER_URL`: the server's address, `https://…` (Android refuses plain `http`).

   The next CI run puts both into the APK.
7. **Server.** Run it with `Auth__Google__ClientIds__0=<web client id>` and its PostgreSQL
   connection string, behind HTTPS.

## Checking it

- CI fails the APK build if the plugin or Credential Manager is missing from it.
- On a phone, the Account tab should show "Sign in with Google" enabled:
  - "isn't set up in this build" means the client id variable was empty;
  - "works in the Android app" means the plugin isn't in the APK.
- If Google's sheet doesn't appear or says the app isn't configured, check the package name and
  SHA-1 on the Android client, and that your account is a test user.
