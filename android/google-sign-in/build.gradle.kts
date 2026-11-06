// A Godot 4 Android plugin (v2): Google sign-in through Android's Credential Manager.
// Built in CI into an AAR that the export plugin in client/addons/chaos_google_sign_in adds to the APK.
plugins {
    id("com.android.library") version "8.5.2"
    id("org.jetbrains.kotlin.android") version "1.9.24"
}

android {
    namespace = "com.chaosidle.googlesignin"
    compileSdk = 34

    defaultConfig {
        minSdk = 24
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

// Keep these in step with _get_android_dependencies() in client/addons/chaos_google_sign_in/plugin.gd:
// the AAR carries only our code, the APK build pulls the libraries.
dependencies {
    compileOnly("org.godotengine:godot:4.7.2.stable")
    implementation("androidx.credentials:credentials:1.3.0")
    implementation("androidx.credentials:credentials-play-services-auth:1.3.0")
    implementation("com.google.android.libraries.identity.googleid:googleid:1.1.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
}
