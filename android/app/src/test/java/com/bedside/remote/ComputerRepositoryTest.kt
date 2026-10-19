package com.bedside.remote

import kotlinx.coroutines.launch
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.runTest
import org.junit.Assert.*
import org.junit.Test

class ComputerRepositoryTest {
    private val first = PairedComputer("one", "PC", "10.0.0.1", 8765, "ab".repeat(32), "secret-one")
    private val second = first.copy(deviceId = "two", host = "10.0.0.2", token = "secret-two")

    @Test
    fun overlappingForgetAndPairCannotResurrectCredentials() = runTest {
        var disk = listOf(first)
        var visible = disk
        val repository =
            ComputerRepository({ disk }, { disk = it }, StandardTestDispatcher(testScheduler))
        val forget = launch {
            repository.update(
                { devices -> devices.filterNot { it.deviceId == first.deviceId } },
                { visible = it },
            )
        }
        val pair = launch { repository.update({ devices -> devices + second }, { visible = it }) }
        forget.join()
        pair.join()
        assertEquals(listOf(second), disk)
        assertEquals(disk, visible)
    }

    @Test
    fun failedSaveNeverPublishesUncommittedCredentials() = runTest {
        var visible = listOf(first)
        val repository =
            ComputerRepository(
                { listOf(first) },
                { throw java.io.IOException("disk unavailable") },
                StandardTestDispatcher(testScheduler),
            )
        val result = runCatching { repository.update({ emptyList() }, { visible = it }) }
        assertTrue(result.isFailure)
        assertEquals(listOf(first), visible)
    }
}
