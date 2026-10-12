// Tailwind classes shared by the table-style views (Manage Songs, Manage Presets).
export const inputClass =
  'w-full rounded border border-input bg-background px-1.5 py-0.5 text-[12.5px] outline-none focus:border-ring focus:ring-1 focus:ring-ring disabled:cursor-not-allowed disabled:opacity-50'

// Multiline: credits support newlines in-game.
export const textareaClass = `${inputClass} resize-none overflow-hidden whitespace-pre [field-sizing:content]`

const buttonClass =
  'inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-[12.5px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60'

export const toolbarButtonClass = `${buttonClass} border-input bg-background hover:bg-muted`

export const secondaryButtonClass = `${buttonClass} border-transparent bg-secondary text-secondary-foreground hover:bg-secondary/80`
