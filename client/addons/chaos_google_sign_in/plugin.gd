@tool
extends EditorPlugin

# Adds the Google sign-in Android plugin to Android exports. The AAR is built from
# android/google-sign-in (CI copies it into bin/). The game uses it through the
# "ChaosIdleGoogleSignIn" singleton; see client/GoogleSignIn.cs.

var _export_plugin: AndroidExportPlugin


func _enter_tree() -> void:
	_export_plugin = AndroidExportPlugin.new()
	add_export_plugin(_export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(_export_plugin)
	_export_plugin = null


class AndroidExportPlugin extends EditorExportPlugin:
	func _get_name() -> String:
		return "ChaosIdleGoogleSignIn"

	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformAndroid

	func _get_android_libraries(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		return PackedStringArray(["chaos_google_sign_in/bin/google-sign-in-release.aar"])

	# Keep in step with android/google-sign-in/build.gradle.kts.
	func _get_android_dependencies(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		return PackedStringArray([
			"androidx.credentials:credentials:1.3.0",
			"androidx.credentials:credentials-play-services-auth:1.3.0",
			"com.google.android.libraries.identity.googleid:googleid:1.1.1",
			"org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1",
		])
