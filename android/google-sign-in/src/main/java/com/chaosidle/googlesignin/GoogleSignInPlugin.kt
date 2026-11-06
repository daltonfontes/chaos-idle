package com.chaosidle.googlesignin

import androidx.credentials.ClearCredentialStateRequest
import androidx.credentials.CredentialManager
import androidx.credentials.CustomCredential
import androidx.credentials.GetCredentialRequest
import androidx.credentials.exceptions.GetCredentialCancellationException
import androidx.credentials.exceptions.GetCredentialException
import androidx.credentials.exceptions.NoCredentialException
import com.google.android.libraries.identity.googleid.GetGoogleIdOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential
import com.google.android.libraries.identity.googleid.GoogleIdTokenParsingException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.godotengine.godot.Godot
import org.godotengine.godot.plugin.GodotPlugin
import org.godotengine.godot.plugin.SignalInfo
import org.godotengine.godot.plugin.UsedByGodot

/**
 * Google sign-in for the game. The game calls [signIn] with our Web client id; Android shows
 * its account sheet; the result comes back as a Godot signal:
 * - `sign_in_succeeded(id_token, email)`: send the ID token to the game server, which checks it;
 * - `sign_in_failed(reason)`: "cancelled", "no_account", or a message.
 * The ID token is never checked or kept here.
 */
class GoogleSignInPlugin(godot: Godot) : GodotPlugin(godot) {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    override fun getPluginName() = "ChaosIdleGoogleSignIn"

    override fun getPluginSignals(): Set<SignalInfo> = setOf(
        SignalInfo(SUCCEEDED, String::class.java, String::class.java),
        SignalInfo(FAILED, String::class.java),
    )

    /**
     * @param serverClientId the OAuth **Web** client id; the server accepts tokens for it.
     * @param onlyAuthorized true to offer only accounts that signed in to this app before (silent sign-in).
     */
    @UsedByGodot
    fun signIn(serverClientId: String, onlyAuthorized: Boolean) {
        val activity = activity ?: return fail("no_activity")
        val option = GetGoogleIdOption.Builder()
            .setServerClientId(serverClientId)
            .setFilterByAuthorizedAccounts(onlyAuthorized)
            .setAutoSelectEnabled(onlyAuthorized)
            .build()
        val request = GetCredentialRequest.Builder().addCredentialOption(option).build()

        scope.launch {
            try {
                val credential = CredentialManager.create(activity).getCredential(activity, request).credential
                if (credential is CustomCredential && credential.type == GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL) {
                    val google = GoogleIdTokenCredential.createFrom(credential.data)
                    emit(SUCCEEDED, google.idToken, google.id)
                } else {
                    fail("unexpected_credential")
                }
            } catch (e: GetCredentialCancellationException) {
                fail("cancelled")
            } catch (e: NoCredentialException) {
                fail("no_account")
            } catch (e: GoogleIdTokenParsingException) {
                fail("bad_token")
            } catch (e: GetCredentialException) {
                fail(e.message ?: e.type)
            }
        }
    }

    /** Forgets the chosen account on this phone, so the next sign-in shows the account sheet again. */
    @UsedByGodot
    fun signOut() {
        val activity = activity ?: return
        scope.launch {
            try {
                CredentialManager.create(activity).clearCredentialState(ClearCredentialStateRequest())
            } catch (e: Exception) {
                // Nothing to clear, or Play services missing: signing out of the game still works.
            }
        }
    }

    private fun fail(reason: String) = emit(FAILED, reason)

    // Signals go to Godot on its own thread.
    private fun emit(name: String, vararg args: Any) = runOnRenderThread { emitSignal(name, *args) }

    private companion object {
        const val SUCCEEDED = "sign_in_succeeded"
        const val FAILED = "sign_in_failed"
    }
}
