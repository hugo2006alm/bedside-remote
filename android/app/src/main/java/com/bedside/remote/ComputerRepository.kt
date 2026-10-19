package com.bedside.remote

import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

/** Publication remains inside the transaction, so an older save cannot overwrite newer UI state. */
class ComputerRepository(
    private val read: () -> List<PairedComputer>,
    private val write: (List<PairedComputer>) -> Unit,
    private val dispatcher: CoroutineDispatcher = Dispatchers.IO,
) {
    private val mutex = Mutex()

    suspend fun load(publish: (List<PairedComputer>) -> Unit) =
        mutex.withLock { publish(withContext(dispatcher) { read() }) }

    suspend fun update(
        transform: (List<PairedComputer>) -> List<PairedComputer>,
        publish: (List<PairedComputer>) -> Unit,
    ) =
        mutex.withLock {
            val next = transform(withContext(dispatcher) { read() })
            withContext(dispatcher) { write(next) }
            publish(next)
        }
}
