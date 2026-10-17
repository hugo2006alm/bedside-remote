package com.bedside.remote

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import kotlinx.serialization.encodeToString

/** Tokens never enter unencrypted preferences, backups, logs, or Intent extras. */
class SecureStore(context: Context) {
    private val preferences = context.getSharedPreferences("bedside", Context.MODE_PRIVATE)
    private val alias = "bedside-device-credentials-v1"

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(alias, null) as? SecretKey)?.let {
            return it
        }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
            .apply {
                init(
                    KeyGenParameterSpec.Builder(
                            alias,
                            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                        )
                        .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                        .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                        .build()
                )
            }
            .generateKey()
    }

    fun computers(): List<PairedComputer> {
        val stored = preferences.getString("computers", null) ?: return emptyList()
        val data = Base64.decode(stored, Base64.NO_WRAP)
        require(data.size > 28) {
            "Stored credentials are invalid. Clear app storage and pair again."
        }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, data.copyOfRange(0, 12)))
        cipher.updateAAD(alias.toByteArray())
        return WireJson.decodeFromString(
            cipher.doFinal(data.copyOfRange(12, data.size)).toString(Charsets.UTF_8)
        )
    }

    fun save(computers: List<PairedComputer>) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        cipher.updateAAD(alias.toByteArray())
        val encrypted = cipher.iv + cipher.doFinal(WireJson.encodeToString(computers).toByteArray())
        check(
            preferences
                .edit()
                .putString("computers", Base64.encodeToString(encrypted, Base64.NO_WRAP))
                .commit()
        ) {
            "Could not save paired computers"
        }
    }

    var haptics: Boolean
        get() = preferences.getBoolean("haptics", false)
        set(value) {
            preferences.edit().putBoolean("haptics", value).apply()
        }

    var reducedMotion: Boolean
        get() = preferences.getBoolean("reducedMotion", false)
        set(value) {
            preferences.edit().putBoolean("reducedMotion", value).apply()
        }

    var lightTheme: Boolean
        get() = preferences.getBoolean("lightTheme", false)
        set(value) {
            preferences.edit().putBoolean("lightTheme", value).apply()
        }

    var sensitivity: Float
        get() = preferences.getFloat("sensitivity", 1.5f)
        set(value) {
            preferences.edit().putFloat("sensitivity", value).apply()
        }
}
