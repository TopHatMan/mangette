<template>
    <div class="flex flex-col gap-3">
        <p v-if="!fileLibraries?.length" class="text-muted text-sm">
            No library yet. Set the library folder above, or add one here.
        </p>
        <div v-for="l in drafts" :key="l.key" class="flex max-sm:flex-col flex-row gap-2 items-stretch sm:items-end">
            <UFormField label="Name" class="sm:w-48">
                <UInput v-model="l.libraryName" class="w-full" />
            </UFormField>
            <UFormField label="Folder" class="grow">
                <UInput v-model="l.basePath" class="w-full" />
            </UFormField>
            <div class="flex gap-2">
                <UButton :loading="busyKey === l.key" class="w-fit" @click="saveLibrary(l)">Save</UButton>
                <UButton color="warning" variant="outline" :loading="busyKey === l.key" class="w-fit" @click="deleteLibrary(l)">
                    Delete
                </UButton>
            </div>
        </div>
        <p v-if="libraryMessage" class="text-sm">{{ libraryMessage }}</p>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type FileLibrary = components['schemas']['FileLibrary'];
const { $api } = useNuxtApp();

const { data: fileLibraries } = await useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });

const drafts = ref<{ key: string; libraryName: string; basePath: string }[]>([]);
watch(
    fileLibraries,
    (list) => {
        drafts.value = (list ?? []).map((l) => ({ key: l.key, libraryName: l.libraryName, basePath: l.basePath }));
    },
    { immediate: true },
);

const busyKey = ref<string | null>(null);
const libraryMessage = ref('');

const saveLibrary = async (library: { key: string; libraryName: string; basePath: string }) => {
    busyKey.value = library.key;
    libraryMessage.value = '';
    try {
        await $api('/v2/FileLibrary/{FileLibraryId}', {
            path: { FileLibraryId: library.key },
            method: 'PATCH',
            body: { path: library.basePath, name: library.libraryName },
        });
        await refreshNuxtData(FetchKeys.FileLibraries);
        libraryMessage.value = 'Library updated. Folder is created if it did not exist.';
    } catch {
        libraryMessage.value = 'Could not update library.';
    } finally {
        busyKey.value = null;
    }
};

const deleteLibrary = async (library: FileLibrary | { key: string }) => {
    busyKey.value = library.key;
    libraryMessage.value = '';
    try {
        await $api('/v2/FileLibrary/{FileLibraryId}', { path: { FileLibraryId: library.key }, method: 'DELETE' });
        await refreshNuxtData(FetchKeys.FileLibraries);
    } catch {
        libraryMessage.value = 'Could not delete library.';
    } finally {
        busyKey.value = null;
    }
};
</script>
