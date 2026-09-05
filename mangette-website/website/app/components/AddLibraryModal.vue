<template>
    <UModal v-bind="$props" title="Add Library">
        <template #body>
            <div class="flex flex-col gap-2">
                <UFormField label="Library Name" required>
                    <UInput v-model="name" placeholder="Name for the library" class="w-full" :disabled="busy" />
                </UFormField>
                <UFormField label="Directory Path" required hint="Folder is created if it does not exist.">
                    <UInput v-model="path" placeholder="C:\\Manga or /mnt/manga" class="w-full" :disabled="busy" />
                </UFormField>
                <UFormField label="Kind" required hint="What this library holds. Cannot be changed after creation.">
                    <USelect v-model="kind" :items="['Manga', 'Comic']" class="w-full" :disabled="busy" />
                </UFormField>
                <UButton icon="i-lucide-plus" :loading="busy" class="w-fit" @click="onAddClick">Add</UButton>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type CreateLibraryRecord = components['schemas']['CreateLibraryRecord'];
const { $api } = useNuxtApp();

const name = ref('');
const path = ref('');
const kind = ref<'Manga' | 'Comic'>('Manga');

const model: ComputedRef = computed((): CreateLibraryRecord => {
    return { basePath: path.value, libraryName: name.value, kind: kind.value };
});

const busy = ref(false);
const onAddClick = async () => {
    if (!model.value) return;
    busy.value = true;
    await $api('/v2/FileLibrary', { method: 'PUT', body: model.value });
    await refreshNuxtData(FetchKeys.FileLibraries);
    name.value = '';
    path.value = '';
    kind.value = 'Manga';
    busy.value = false;
};
</script>
