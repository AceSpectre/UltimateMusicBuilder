import { addMessages, init, locale } from 'svelte-i18n'
import en from './en'

addMessages('en', en)

init({
  fallbackLocale: 'en',
  initialLocale: 'en'
})

void locale.set('en')
