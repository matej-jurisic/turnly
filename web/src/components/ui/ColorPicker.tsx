import { AVATAR_COLORS, cn } from '@/lib/utils'

export function ColorPicker({ value, onChange }: { value: string; onChange: (color: string) => void }) {
  return (
    <div className="flex flex-wrap gap-2">
      {AVATAR_COLORS.map((color) => (
        <button
          key={color}
          type="button"
          onClick={() => onChange(color)}
          className={cn(
            'h-8 w-8 rounded-full border-2 transition',
            value.toLowerCase() === color.toLowerCase()
              ? 'border-foreground ring-2 ring-ring'
              : 'border-transparent',
          )}
          style={{ backgroundColor: color }}
          aria-label={`Select color ${color}`}
        />
      ))}
    </div>
  )
}
