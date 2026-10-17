package com.bedside.remote

import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.runtime.LaunchedEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle

class MainActivity : ComponentActivity() {
    private val remote: RemoteViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        if (savedInstanceState == null) handleShare(intent)
        setContent {
            val state = remote.state.collectAsStateWithLifecycle().value
            LaunchedEffect(state.lightTheme) {
                val bars =
                    if (state.lightTheme) SystemBarStyle.light(Color.TRANSPARENT, Color.TRANSPARENT)
                    else SystemBarStyle.dark(Color.TRANSPARENT)
                enableEdgeToEdge(statusBarStyle = bars, navigationBarStyle = bars)
            }
            BedsideApp(state, remote)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleShare(intent)
    }

    private fun handleShare(intent: Intent) {
        if (intent.action == Intent.ACTION_SEND && intent.type == "text/plain")
            remote.receiveShare(intent.getStringExtra(Intent.EXTRA_TEXT).orEmpty())
    }

    override fun onStart() {
        super.onStart()
        remote.onForeground()
    }

    override fun onStop() {
        remote.onBackground()
        super.onStop()
    }
}
