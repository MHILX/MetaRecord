type EditorPrimaryNavPage = 'workflow' | 'metadata';

interface EditorPrimaryNavProps {
  activePage: EditorPrimaryNavPage;
}

const navItems: Array<{ page: EditorPrimaryNavPage; label: string; href: string }> = [
  { page: 'workflow', label: 'Workflow', href: '#workflow' },
  { page: 'metadata', label: 'Metadata', href: '#metadata' }
];

export function EditorPrimaryNav({ activePage }: EditorPrimaryNavProps) {
  return (
    <nav className="primary-nav" aria-label="Primary pages">
      {navItems.map(item => {
        const isActive = item.page === activePage;

        return (
          <a
            key={item.page}
            className="primary-nav-link"
            href={item.href}
            aria-current={isActive ? 'page' : undefined}
          >
            {item.label}
          </a>
        );
      })}
    </nav>
  );
}